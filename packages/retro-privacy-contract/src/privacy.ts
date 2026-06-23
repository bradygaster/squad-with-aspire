// retro-privacy-contract: framework-agnostic pure helpers backing TR-008 §7 assertions.
// Contract source: docs/security/retro-privacy.md (commit d476636).
// No I/O, no timers. Mirrors auth-ui-contracts / monitor-degrade-contract style.

import { createHash } from "node:crypto";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface RawPost {
  readonly post_id: string;
  readonly retro_id: string;
  readonly author_id: string;
  readonly raw_text: string;
}

export interface AuditEvent {
  readonly event:
    | "raw_post.created"
    | "raw_post.read"
    | "raw_post.purged"
    | "raw_post.access_denied"
    | "key.rotated"
    | "aggregate.rendered";
  readonly ts: string;
  readonly retro_id: string;
  readonly post_id?: string;
  readonly viewer_id?: string;
  readonly viewer_role?: "facilitator" | "author" | "system";
  readonly reason_code?: string;
  readonly prev_hash: string;
  readonly self_hash: string;
}

export interface SentimentCategory {
  readonly label: string;
  readonly cohort: readonly string[]; // author_ids in this bucket
}

// ---------------------------------------------------------------------------
// §7 #1 + #5 — Jaccard similarity for transcript / action-item leak detection
// ---------------------------------------------------------------------------

// Token set: lowercase, alnum-run split, drop tokens length < 3 to avoid
// trivial overlap on stopwords.
export function tokenize(text: string): Set<string> {
  const toks = text
    .toLowerCase()
    .split(/[^a-z0-9]+/u)
    .filter((t) => t.length >= 3);
  return new Set(toks);
}

export function jaccardSimilarity(a: string, b: string): number {
  const A = tokenize(a);
  const B = tokenize(b);
  if (A.size === 0 && B.size === 0) return 0;
  let inter = 0;
  for (const t of A) if (B.has(t)) inter++;
  const union = A.size + B.size - inter;
  return union === 0 ? 0 : inter / union;
}

export const TRANSCRIPT_LEAK_THRESHOLD = 0.3;
export const ACTION_ITEM_LEAK_THRESHOLD = 0.5;

export function transcriptLeaksRaw(transcript: string, posts: readonly RawPost[]): boolean {
  return posts.some((p) => jaccardSimilarity(transcript, p.raw_text) > TRANSCRIPT_LEAK_THRESHOLD);
}

export function actionItemLeaksRaw(body: string, posts: readonly RawPost[]): boolean {
  return posts.some((p) => jaccardSimilarity(body, p.raw_text) > ACTION_ITEM_LEAK_THRESHOLD);
}

// ---------------------------------------------------------------------------
// §7 #4 — Singleton cohort collapse (D5-3, min size = 2)
// ---------------------------------------------------------------------------

export const MIN_COHORT_SIZE = 2;
export const OTHER_LABEL = "other";

export function collapseSingletons(
  categories: readonly SentimentCategory[],
): readonly SentimentCategory[] {
  const kept: SentimentCategory[] = [];
  const otherAuthors: string[] = [];
  for (const c of categories) {
    if (c.cohort.length < MIN_COHORT_SIZE) {
      otherAuthors.push(...c.cohort);
    } else {
      kept.push(c);
    }
  }
  if (otherAuthors.length === 0) return kept;
  // Singletons that merge into "other" stay below threshold individually; the
  // merged bucket is only published if it itself meets cohort size, otherwise
  // it is dropped entirely to prevent 1-IC de-anonymization via "other".
  if (otherAuthors.length < MIN_COHORT_SIZE) return kept;
  return [...kept, { label: OTHER_LABEL, cohort: otherAuthors }];
}

// ---------------------------------------------------------------------------
// §3.1 — AAD binding for AES-256-GCM raw-post encryption
// ---------------------------------------------------------------------------

// AAD = retro_id || post_id || author_id. Order is fixed and part of the
// contract: swapping any field must produce a different AAD so a ciphertext
// re-bound to a different (retro,post,author) tuple fails authentication.
export function buildAad(retro_id: string, post_id: string, author_id: string): Buffer {
  if (!retro_id || !post_id || !author_id) {
    throw new Error("AAD inputs must be non-empty");
  }
  return Buffer.from(`${retro_id}||${post_id}||${author_id}`, "utf8");
}

// ---------------------------------------------------------------------------
// §4 / §7 #8 — Audit log hash chain
// ---------------------------------------------------------------------------

export const GENESIS_HASH = "sha256:" + "0".repeat(64);

function hashChainEntry(prevHash: string, payload: Omit<AuditEvent, "prev_hash" | "self_hash">): string {
  const canonical = JSON.stringify({ prev_hash: prevHash, ...payload }, Object.keys(payload).sort());
  return "sha256:" + createHash("sha256").update(canonical).digest("hex");
}

export function appendAuditEvent(
  chain: readonly AuditEvent[],
  payload: Omit<AuditEvent, "prev_hash" | "self_hash">,
): AuditEvent {
  const prev = chain.length === 0 ? GENESIS_HASH : chain[chain.length - 1].self_hash;
  const self_hash = hashChainEntry(prev, payload);
  return { ...payload, prev_hash: prev, self_hash };
}

export interface ChainVerificationResult {
  readonly ok: boolean;
  readonly brokenAt?: number; // index of first bad entry
  readonly reason?: "prev_mismatch" | "self_hash_mismatch" | "genesis_mismatch";
}

export function verifyAuditChain(chain: readonly AuditEvent[]): ChainVerificationResult {
  for (let i = 0; i < chain.length; i++) {
    const entry = chain[i];
    const expectedPrev = i === 0 ? GENESIS_HASH : chain[i - 1].self_hash;
    if (entry.prev_hash !== expectedPrev) {
      return { ok: false, brokenAt: i, reason: i === 0 ? "genesis_mismatch" : "prev_mismatch" };
    }
    const { prev_hash: _p, self_hash: _s, ...payload } = entry;
    const expectedSelf = hashChainEntry(entry.prev_hash, payload);
    if (entry.self_hash !== expectedSelf) {
      return { ok: false, brokenAt: i, reason: "self_hash_mismatch" };
    }
  }
  return { ok: true };
}

// ---------------------------------------------------------------------------
// §4 / §7 #3 — Read-audit policy (facilitator yes, author self-read no)
// ---------------------------------------------------------------------------

export function shouldAuditRead(viewer_id: string, author_id: string, viewer_role: "facilitator" | "author" | "system"): boolean {
  if (viewer_role === "author" && viewer_id === author_id) return false; // §4: author self-read NOT audited
  if (viewer_role === "system") return false; // internal reducer reads
  return true; // facilitator (or any non-author principal) always audited
}

// ---------------------------------------------------------------------------
// §7 #6 — Cross-retro guard
// ---------------------------------------------------------------------------

export interface FacilitatorClaim {
  readonly viewer_id: string;
  readonly viewer_role: "facilitator" | "author" | "system";
  readonly retro_id_claim: string;
  readonly issued_at_epoch_ms: number;
}

export interface ReadOutcome {
  readonly status: 200 | 403;
  readonly auditEventType?: AuditEvent["event"];
}

export const FACILITATOR_FRESHNESS_MS = 5 * 60 * 1000; // §2 S row: ≤5min step-up

export function authorizeRead(
  claim: FacilitatorClaim,
  post: RawPost,
  now_epoch_ms: number,
): ReadOutcome {
  if (claim.retro_id_claim !== post.retro_id) {
    return { status: 403, auditEventType: "raw_post.access_denied" };
  }
  if (claim.viewer_role === "facilitator") {
    const age = now_epoch_ms - claim.issued_at_epoch_ms;
    if (age < 0 || age > FACILITATOR_FRESHNESS_MS) {
      return { status: 403, auditEventType: "raw_post.access_denied" };
    }
  }
  return {
    status: 200,
    auditEventType: shouldAuditRead(claim.viewer_id, post.author_id, claim.viewer_role)
      ? "raw_post.read"
      : undefined,
  };
}

// ---------------------------------------------------------------------------
// §7 #2 — Purge invariants (pure check; runtime side performs the wipe)
// ---------------------------------------------------------------------------

export interface StoredCiphertext {
  readonly post_id: string;
  readonly retro_id: string;
  readonly ciphertext: Uint8Array;
  readonly purged: boolean;
}

export function isProperlyPurged(row: StoredCiphertext): boolean {
  if (!row.purged) return false;
  // After purge, ciphertext blob MUST be all zero-bytes (defense vs tombstone recovery).
  for (let i = 0; i < row.ciphertext.length; i++) {
    if (row.ciphertext[i] !== 0) return false;
  }
  return true;
}

// ---------------------------------------------------------------------------
// §3.3 / §7 #7 — Key rotation: keyed by version byte prefix
// ---------------------------------------------------------------------------

export interface WrappedDataKey {
  readonly keyVersion: number; // single-byte prefix per §3.3
  readonly wrappedBytes: Uint8Array;
}

export function canDecryptUnderKeyset(
  wdk: WrappedDataKey,
  availableMasterKeyVersions: readonly number[],
): boolean {
  return availableMasterKeyVersions.includes(wdk.keyVersion);
}
