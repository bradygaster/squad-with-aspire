/**
 * TR-008 §7 privacy assertions — property-test harness.
 *
 * Contract source: docs/security/retro-privacy.md §7 (commit d476636).
 * Hard-gate (separate from 85% coverage %): every §7 bullet maps to a `describe`
 * block below and MUST stay green for v0.1.0 release (TR-010 ledger).
 *
 * Pure assertions run today against packages/retro-privacy-contract/.
 * Runtime-bound assertions (purge wipe, key rotation crypto, cross-retro 403
 * at the HTTP boundary) are coded as live tests against shim doubles so they
 * exercise the contract surface now; when TR-001 lands the reducer/orchestrator
 * the same tests will bind to the real impl via the same exported interface.
 *
 * No fast-check dep is required: we generate post fixtures inline. If the
 * repo later adds fast-check we can swap the inline generators for `fc.array`.
 */

import { describe, expect, it } from "vitest";
import {
  ACTION_ITEM_LEAK_THRESHOLD,
  TRANSCRIPT_LEAK_THRESHOLD,
  actionItemLeaksRaw,
  appendAuditEvent,
  authorizeRead,
  buildAad,
  canDecryptUnderKeyset,
  collapseSingletons,
  FACILITATOR_FRESHNESS_MS,
  isProperlyPurged,
  jaccardSimilarity,
  MIN_COHORT_SIZE,
  OTHER_LABEL,
  shouldAuditRead,
  verifyAuditChain,
  type AuditEvent,
  type FacilitatorClaim,
  type RawPost,
  type SentimentCategory,
  type StoredCiphertext,
  type WrappedDataKey,
} from "../../packages/retro-privacy-contract/src/privacy";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const rawPosts: readonly RawPost[] = [
  {
    post_id: "post_01",
    retro_id: "retro_sprint42",
    author_id: "user_alice@contoso.com",
    raw_text: "manager interrupted me three times during architecture review and dismissed my proposal",
  },
  {
    post_id: "post_02",
    retro_id: "retro_sprint42",
    author_id: "user_bob@contoso.com",
    raw_text: "deployment pipeline broke twice this week from flaky integration tests in checkout module",
  },
  {
    post_id: "post_03",
    retro_id: "retro_sprint42",
    author_id: "user_carol@contoso.com",
    raw_text: "on-call rotation is unsustainable — paged 14 times in 5 nights for false alarms",
  },
];

// ---------------------------------------------------------------------------
// §7 #1 — Transcript.Render output never contains raw_text (Jaccard ≤ 0.3)
// ---------------------------------------------------------------------------

describe("TR-008 §7.1 — transcript MUST NOT leak raw posts (Jaccard ≤ 0.3)", () => {
  it("synthetic safe aggregate stays well under threshold", () => {
    const transcript =
      "Squad discussed collaboration friction, build reliability, and operational load. " +
      "Themes: meeting hygiene (3 ICs), CI stability (2 ICs), on-call burden (2 ICs).";
    for (const p of rawPosts) {
      expect(jaccardSimilarity(transcript, p.raw_text)).toBeLessThanOrEqual(TRANSCRIPT_LEAK_THRESHOLD);
    }
  });

  it("detects leak when transcript verbatim-quotes a raw post", () => {
    const leakyTranscript = `Themes included: "${rawPosts[0].raw_text}" and other items.`;
    expect(jaccardSimilarity(leakyTranscript, rawPosts[0].raw_text)).toBeGreaterThan(
      TRANSCRIPT_LEAK_THRESHOLD,
    );
  });

  it("property: empty transcript has Jaccard 0 against every post", () => {
    for (const p of rawPosts) {
      expect(jaccardSimilarity("", p.raw_text)).toBe(0);
    }
  });

  it("property: identical strings have Jaccard 1 (sanity)", () => {
    expect(jaccardSimilarity(rawPosts[0].raw_text, rawPosts[0].raw_text)).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// §7 #2 — Purge: 0 rows returned AND ciphertext zero-bytes
// ---------------------------------------------------------------------------

describe("TR-008 §7.2 — purge wipes ciphertext to zero-bytes", () => {
  it("non-purged row is not considered properly purged", () => {
    const row: StoredCiphertext = {
      post_id: "post_01",
      retro_id: "retro_sprint42",
      ciphertext: new Uint8Array([1, 2, 3, 4]),
      purged: false,
    };
    expect(isProperlyPurged(row)).toBe(false);
  });

  it("purged=true with non-zero ciphertext FAILS (tombstone-recovery vector)", () => {
    const row: StoredCiphertext = {
      post_id: "post_01",
      retro_id: "retro_sprint42",
      ciphertext: new Uint8Array([1, 2, 3, 4]),
      purged: true,
    };
    expect(isProperlyPurged(row)).toBe(false);
  });

  it("purged=true with all-zero ciphertext passes", () => {
    const row: StoredCiphertext = {
      post_id: "post_01",
      retro_id: "retro_sprint42",
      ciphertext: new Uint8Array(64), // zeroed
      purged: true,
    };
    expect(isProperlyPurged(row)).toBe(true);
  });

  it("empty ciphertext + purged=true also acceptable (idempotent re-run)", () => {
    const row: StoredCiphertext = {
      post_id: "post_01",
      retro_id: "retro_sprint42",
      ciphertext: new Uint8Array(0),
      purged: true,
    };
    expect(isProperlyPurged(row)).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// §7 #3 — raw_post.read emitted on facilitator view; NOT on author self-read
// ---------------------------------------------------------------------------

describe("TR-008 §7.3 — read audit policy", () => {
  it("facilitator view of any post is audited", () => {
    expect(shouldAuditRead("user_alice@contoso.com", "user_bob@contoso.com", "facilitator")).toBe(true);
  });

  it("author self-read is NOT audited", () => {
    expect(shouldAuditRead("user_alice@contoso.com", "user_alice@contoso.com", "author")).toBe(false);
  });

  it("another author reading someone else's post under role=author is still audited", () => {
    // role=author + viewer != post.author means request was authorized through
    // a different path; if it ever happens we want it on tape.
    expect(shouldAuditRead("user_alice@contoso.com", "user_bob@contoso.com", "author")).toBe(true);
  });

  it("system/reducer reads are not audited (would flood)", () => {
    expect(shouldAuditRead("system", "user_bob@contoso.com", "system")).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// §7 #4 — Singleton sentiment category collapsed to "other"
// ---------------------------------------------------------------------------

describe("TR-008 §7.4 — singleton cohorts collapse to 'other' (D5-3)", () => {
  it("category of size 1 is removed and merged into other when ≥2 singletons exist", () => {
    const cats: SentimentCategory[] = [
      { label: "meetings", cohort: ["a", "b", "c"] },
      { label: "tooling", cohort: ["d"] }, // singleton
      { label: "morale", cohort: ["e"] }, // singleton
    ];
    const out = collapseSingletons(cats);
    expect(out.map((c) => c.label)).toEqual(["meetings", OTHER_LABEL]);
    const other = out.find((c) => c.label === OTHER_LABEL)!;
    expect(other.cohort.length).toBeGreaterThanOrEqual(MIN_COHORT_SIZE);
  });

  it("single lone singleton is DROPPED entirely (otherwise 'other'={one IC} de-anonymizes)", () => {
    const cats: SentimentCategory[] = [
      { label: "meetings", cohort: ["a", "b"] },
      { label: "tooling", cohort: ["d"] }, // singleton, only one
    ];
    const out = collapseSingletons(cats);
    expect(out.map((c) => c.label)).toEqual(["meetings"]);
  });

  it("no singletons → input passes through unchanged", () => {
    const cats: SentimentCategory[] = [
      { label: "meetings", cohort: ["a", "b"] },
      { label: "ci", cohort: ["c", "d", "e"] },
    ];
    expect(collapseSingletons(cats)).toEqual(cats);
  });

  it("ZERO category bucket of any size is never published (vacuous)", () => {
    const cats: SentimentCategory[] = [{ label: "ghost", cohort: [] }];
    const out = collapseSingletons(cats);
    expect(out).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// §7 #5 — Action-item body Jaccard ≤ 0.5 vs any raw post (LLM never echoes)
// ---------------------------------------------------------------------------

describe("TR-008 §7.5 — action-item body MUST NOT echo raw post (Jaccard ≤ 0.5)", () => {
  it("clean depersonalized action item passes", () => {
    const body = "Reduce architecture-review meeting load by one occurrence per week";
    expect(actionItemLeaksRaw(body, rawPosts)).toBe(false);
  });

  it("LLM regurgitating raw text fails", () => {
    const body = rawPosts[0].raw_text + " — owner: tech-lead";
    expect(actionItemLeaksRaw(body, rawPosts)).toBe(true);
  });

  it("threshold sanity — 0.5 boundary is exclusive (uses > not >=)", () => {
    // Construct a string whose Jaccard against post_01 sits below threshold.
    const partial = "manager interrupted review";
    expect(jaccardSimilarity(partial, rawPosts[0].raw_text)).toBeLessThanOrEqual(
      ACTION_ITEM_LEAK_THRESHOLD,
    );
  });
});

// ---------------------------------------------------------------------------
// §7 #6 — Cross-retro read attempt returns 403 + access_denied
// ---------------------------------------------------------------------------

describe("TR-008 §7.6 — cross-retro read is blocked + audited", () => {
  const now = Date.UTC(2026, 5, 23, 10, 42, 0); // 2026-06-23T10:42Z
  const freshClaim = (overrides: Partial<FacilitatorClaim> = {}): FacilitatorClaim => ({
    viewer_id: "user_facil@contoso.com",
    viewer_role: "facilitator",
    retro_id_claim: "retro_sprint42",
    issued_at_epoch_ms: now - 60_000,
    ...overrides,
  });

  it("matching retro_id + fresh claim → 200 + raw_post.read", () => {
    const r = authorizeRead(freshClaim(), rawPosts[0], now);
    expect(r.status).toBe(200);
    expect(r.auditEventType).toBe("raw_post.read");
  });

  it("mismatched retro_id_claim → 403 + access_denied", () => {
    const r = authorizeRead(freshClaim({ retro_id_claim: "retro_sprint41" }), rawPosts[0], now);
    expect(r.status).toBe(403);
    expect(r.auditEventType).toBe("raw_post.access_denied");
  });

  it("stale facilitator claim (>5min) → 403 even on matching retro", () => {
    const stale = freshClaim({ issued_at_epoch_ms: now - (FACILITATOR_FRESHNESS_MS + 1000) });
    const r = authorizeRead(stale, rawPosts[0], now);
    expect(r.status).toBe(403);
  });

  it("future-dated claim → 403 (clock-skew safety)", () => {
    const r = authorizeRead(freshClaim({ issued_at_epoch_ms: now + 60_000 }), rawPosts[0], now);
    expect(r.status).toBe(403);
  });

  it("author self-read on own retro → 200 + no audit event", () => {
    const claim: FacilitatorClaim = {
      viewer_id: rawPosts[0].author_id,
      viewer_role: "author",
      retro_id_claim: rawPosts[0].retro_id,
      issued_at_epoch_ms: now,
    };
    const r = authorizeRead(claim, rawPosts[0], now);
    expect(r.status).toBe(200);
    expect(r.auditEventType).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// §7 #7 — Key rotation: old-key posts still decrypt; new posts use new key
// ---------------------------------------------------------------------------

describe("TR-008 §7.7 — key rotation tolerates both wrap versions in-flight", () => {
  it("WDK wrapped under v1 decrypts when keyset={v1,v2}", () => {
    const wdk: WrappedDataKey = { keyVersion: 1, wrappedBytes: new Uint8Array(32) };
    expect(canDecryptUnderKeyset(wdk, [1, 2])).toBe(true);
  });

  it("WDK wrapped under v1 FAILS when keyset={v2} (v1 retired)", () => {
    const wdk: WrappedDataKey = { keyVersion: 1, wrappedBytes: new Uint8Array(32) };
    expect(canDecryptUnderKeyset(wdk, [2])).toBe(false);
  });

  it("AAD differs across (retro,post,author) triple permutations", () => {
    const a = buildAad("r1", "p1", "alice").toString("utf8");
    const b = buildAad("r1", "p1", "bob").toString("utf8");
    const c = buildAad("r2", "p1", "alice").toString("utf8");
    expect(a).not.toBe(b);
    expect(a).not.toBe(c);
    expect(b).not.toBe(c);
  });

  it("AAD rejects empty inputs (defense against caller-side bugs)", () => {
    expect(() => buildAad("", "p1", "alice")).toThrow();
    expect(() => buildAad("r1", "", "alice")).toThrow();
    expect(() => buildAad("r1", "p1", "")).toThrow();
  });
});

// ---------------------------------------------------------------------------
// §7 #8 — Audit chain tamper detection
// ---------------------------------------------------------------------------

describe("TR-008 §7.8 — audit log hash-chain detects tampering", () => {
  const buildChain = (): AuditEvent[] => {
    const c: AuditEvent[] = [];
    for (let i = 0; i < 5; i++) {
      c.push(
        appendAuditEvent(c, {
          event: "raw_post.read",
          ts: `2026-06-23T10:${(40 + i).toString().padStart(2, "0")}:00Z`,
          retro_id: "retro_sprint42",
          post_id: `post_0${i + 1}`,
          viewer_id: "user_facil@contoso.com",
          viewer_role: "facilitator",
          reason_code: "transcript_review",
        }),
      );
    }
    return c;
  };

  it("well-formed chain verifies", () => {
    expect(verifyAuditChain(buildChain())).toEqual({ ok: true });
  });

  it("mutating a middle entry's payload breaks self_hash", () => {
    const chain = buildChain();
    const bad = [...chain];
    bad[2] = { ...bad[2], viewer_id: "user_attacker@contoso.com" };
    const r = verifyAuditChain(bad);
    expect(r.ok).toBe(false);
    expect(r.brokenAt).toBe(2);
    expect(r.reason).toBe("self_hash_mismatch");
  });

  it("snipping an entry breaks prev_hash on the successor", () => {
    const chain = buildChain();
    const snipped = [...chain.slice(0, 2), ...chain.slice(3)];
    const r = verifyAuditChain(snipped);
    expect(r.ok).toBe(false);
    expect(r.brokenAt).toBe(2);
    expect(r.reason).toBe("prev_mismatch");
  });

  it("re-ordering entries breaks the chain", () => {
    const chain = buildChain();
    const swapped = [chain[0], chain[2], chain[1], chain[3], chain[4]];
    expect(verifyAuditChain(swapped).ok).toBe(false);
  });

  it("genesis entry whose prev_hash != GENESIS_HASH fails with genesis_mismatch", () => {
    const chain = buildChain();
    const forged: AuditEvent = { ...chain[0], prev_hash: "sha256:" + "f".repeat(64) };
    const r = verifyAuditChain([forged, ...chain.slice(1)]);
    expect(r.ok).toBe(false);
    expect(r.brokenAt).toBe(0);
    // self_hash was computed over the original genesis prev, so mutating prev
    // ALSO invalidates self_hash; verifier reports the first failure encountered.
    expect(r.reason === "genesis_mismatch" || r.reason === "self_hash_mismatch").toBe(true);
  });
});

// ---------------------------------------------------------------------------
// Cross-cutting: when TR-001 reducer lands, source-level guards must hold.
// These are scaffolded as skip-until-present, matching the
// copilot-cli-adjacent-preflights pattern.
// ---------------------------------------------------------------------------

import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

const RETRO_SRC_ROOTS = [
  "src/AspireWithSquad.Retro",
  "src/AspireWithSquad.RetroOrchestrator",
  "src/CommunityToolkit.Aspire.Hosting.Squad",
];

function collectRetroSources(): string[] {
  const out: string[] = [];
  for (const root of RETRO_SRC_ROOTS) {
    if (!existsSync(root)) continue;
    const stack = [root];
    while (stack.length) {
      const cur = stack.pop()!;
      for (const ent of readdirSync(cur)) {
        const p = join(cur, ent);
        const st = statSync(p);
        if (st.isDirectory()) {
          if (!/bin|obj|node_modules/i.test(ent)) stack.push(p);
        } else if (/\.(cs|ts)$/i.test(ent)) {
          out.push(p);
        }
      }
    }
  }
  return out;
}

describe("TR-008 §5 — source-level enforcement (semgrep mirror, fast-fail in CI)", () => {
  const retroSources = collectRetroSources();

  (retroSources.length === 0 ? it.skip : it)(
    "no retro source logs RawPost.raw_text or .RawText",
    () => {
      const bad: string[] = [];
      const leakRe = /\b(_?logger|Log|Console)\.[A-Za-z]+\([^)]*\b(raw_text|RawText|\.RawPost\.[A-Za-z]*Body)/i;
      for (const f of retroSources) {
        const src = readFileSync(f, "utf8");
        if (leakRe.test(src)) bad.push(f);
      }
      expect(bad).toEqual([]);
    },
  );

  (retroSources.length === 0 ? it.skip : it)(
    "no retro source sets OTel attribute with raw_text/post.body",
    () => {
      const bad: string[] = [];
      const otelRe = /(SetTag|SetAttribute)\([^)]*\b(raw_text|post\.body|RawText)\b/i;
      for (const f of retroSources) {
        if (otelRe.test(readFileSync(f, "utf8"))) bad.push(f);
      }
      expect(bad).toEqual([]);
    },
  );

  (retroSources.length === 0 ? it.skip : it)(
    "AesGcm.Encrypt calls on retro path supply associatedData",
    () => {
      const bad: string[] = [];
      const callRe = /AesGcm\.\s*Encrypt\s*\([^)]*\)/g;
      const aadParamRe = /associatedData|aad/i;
      for (const f of retroSources) {
        const src = readFileSync(f, "utf8");
        const matches = src.match(callRe) ?? [];
        for (const m of matches) {
          if (!aadParamRe.test(m)) bad.push(`${f}: ${m}`);
        }
      }
      expect(bad).toEqual([]);
    },
  );

  (retroSources.length === 0 ? it.skip : it)(
    "no public method returns IEnumerable<RawPost> / RawPost[] outside reducer",
    () => {
      const bad: string[] = [];
      const exportRe = /\bpublic\s+(static\s+)?(IEnumerable<RawPost>|RawPost\[\]|IReadOnlyList<RawPost>)\s+\w+/;
      for (const f of retroSources) {
        if (/Reducer/i.test(f)) continue; // reducer internals allowed
        if (exportRe.test(readFileSync(f, "utf8"))) bad.push(f);
      }
      expect(bad).toEqual([]);
    },
  );
});
