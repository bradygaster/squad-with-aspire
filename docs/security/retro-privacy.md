# Team Retro — Sentiment Privacy & Retention (TR-007)

**Status:** Draft v1 — ready for review
**Owner:** security-hardening-squad
**Refs:** `specs/team-retro/PRD.md` (commit 731b737) §TR-007, D5
**Scope:** STRIDE threat model + encryption/retention/audit controls for retro raw IC posts and sentiment aggregates.

---

## 1. Data classification

| Data class | Examples | Sensitivity | Visibility |
|---|---|---|---|
| **Raw IC post** | Free-text sentiment from one IC ("manager interrupted me 3x") | **HIGH** — psychological-safety risk if leaked | Author + facilitator only, during one retro cycle |
| **Aggregate sentiment** | "3 ICs negative on meetings, 2 positive on shipping" | LOW | Whole squad, shareable transcript |
| **Action item** | "Reduce meeting load by 1/wk" (post-vote, depersonalized) | LOW | Public; becomes GH issue (TR-003) |
| **Author identity binding** | Map(post-id → user-id) | **HIGH** | Encrypted; access audited |
| **Vote record** | Map(item-id → user-id, weight) | MEDIUM | Aggregate-visible; individual votes never displayed |

**Confirming D5 with stricter floor:** aggregate-only in shareable transcripts, raw posts retained **30 days max** AND **purged at retro-cycle close + 7 days**, whichever is sooner. The +7d window covers facilitator follow-up; sprint+2 has no legitimate raw-post need.

---

## 2. STRIDE threat model

### S — Spoofing

| Threat | Vector | Mitigation |
|---|---|---|
| Impersonate IC to submit toxic raw post under their name | Forged Teams/GH webhook payload | Verify Teams JWT / GitHub HMAC signature on every ingestion (TR-002 boundary). Bind `author_id` from signed identity, never from request body. |
| Impersonate facilitator to read raw posts | Stolen session token | Facilitator role required + step-up: read-raw operation re-validates IdP claim freshness ≤5min. |

### T — Tampering

| Threat | Vector | Mitigation |
|---|---|---|
| Modify another IC's raw post pre-aggregation | Direct store write bypassing orchestrator | All writes go through reducer (TR-001); store enforces append-only event log keyed by `(retro_id, event_seq)`. |
| Tamper with aggregate to skew action items | DB write | Aggregates derived deterministically from event log on read; never persisted as authoritative state. Recompute on each transcript render. |
| Tamper with audit log | Operator with store access | Audit log written to separate append-only sink (OTel log signal → log archive bucket with object-lock, 90d retention). Hash-chained: each entry includes SHA-256 of prior entry. |

### R — Repudiation

| Threat | Vector | Mitigation |
|---|---|---|
| IC denies submitting post that became action item | "Wasn't me" | All ingestion events signed by IdP; event log retains signed envelope for full 30d. |
| Facilitator denies viewing raw post | "I never saw it" | Read of `raw_post` MUST emit audit event `raw_post.read{retro_id, post_id, viewer_id, ts, reason_code}`. No bulk-export path. |

### I — Information disclosure  *(primary risk class for TR-007)*

| Threat | Vector | Mitigation |
|---|---|---|
| Raw post leaks via shareable transcript | Reducer accidentally serializes `raw_text` into transcript payload | **Type-level separation**: `RawPost` and `AggregateSentiment` are distinct types; transcript renderer accepts only `AggregateSentiment[]`. Semgrep rule enforced (see §5). |
| Raw post leaks via OTel trace span attrs | Developer adds `span.SetTag("post.text", raw)` for debugging | Semgrep rule blocks `*.text`, `*.raw*`, `*.body` as OTel attribute values in retro module paths. |
| Raw post leaks via log line | `logger.Info("Got post: {Post}", post)` | Same semgrep rule + structured-logging convention: `RawPost.ToString()` returns `"<redacted raw post {id}>"`. |
| Raw post leaks via GH issue body (TR-003 pipeline) | Action-item synthesizer includes verbatim raw text | TR-003 contract: action-item body composed ONLY from voted+approved aggregated text. Reducer rejects `actionItem.create` events where `body` matches any current raw_post by substring (Jaccard ≥0.6 → reject). |
| Encrypted-at-rest bypass | Backup snapshot leaked | Raw posts encrypted with envelope key; data key wrapped by KMS-equivalent. Backups inherit encryption (encrypt-then-snapshot). Local-dev: AES-256-GCM with key from `RETRO_RAW_KEY` env (32-byte, base64); CI rejects empty value. |
| De-anonymization via timing/style | "Only Sarah uses 'tbh'" | Aggregate transcripts present sentiment counts and themes, never verbatim phrases ≥4 tokens. Theme extraction strips stopwords + names. Min cohort size for any category = 2 (singleton categories collapsed into "other"). |
| Replay of old raw posts into new retro | Stale data injected via store snapshot | Each raw post tagged with `retro_id`; reducer rejects events with mismatched `retro_id`. |

### D — Denial of service

| Threat | Vector | Mitigation |
|---|---|---|
| Flood ingestion with spam posts | Compromised IC account or webhook abuse | Rate-limit per `(author_id, retro_id)`: 50 posts/retro hard cap; per-IP 100/hr. Reuses `AspireWithSquad.RateLimiting` (commit f082ca2). |
| Oversize raw post crashes serializer | 10MB string | Per-post `raw_text` cap = 4096 chars enforced at ingestion boundary; reject 413. |

### E — Elevation of privilege

| Threat | Vector | Mitigation |
|---|---|---|
| IC reads other ICs' raw posts | API call with crafted `post_id` | Authorization on read: requester must be `post.author_id` OR have `retro:facilitator` claim for `post.retro_id`. |
| Facilitator reads raw posts from retro they don't own | Cross-retro access | Facilitator claim scoped to specific `retro_id`; orchestrator rejects mismatched scope. |
| Service account reads raw posts to feed action-item LLM | Background pipeline | TR-003 synthesizer receives **aggregates only**. Service account has no `retro:read-raw` capability. Enforced at IAM layer. |

---

## 3. Encryption & retention controls

### 3.1 At-rest encryption (raw posts only)

```
ciphertext = AES-256-GCM(key=data_key, nonce=random96, plaintext=raw_text, aad=retro_id||post_id||author_id)
stored = { post_id, retro_id, author_id, ciphertext, nonce, created_at, encrypted_data_key }
```

- `encrypted_data_key` = data_key wrapped by master key (KMS in prod; env-var-loaded master in dev).
- AAD binding prevents ciphertext-swap attacks across posts.
- Aggregates and action items stored plaintext (low sensitivity).

### 3.2 Retention

| Data | Retention | Trigger |
|---|---|---|
| Raw post ciphertext + author binding | `min(30d, retro_close + 7d)` | Scheduled purge job, idempotent |
| Aggregate sentiment | 1y | Cycle-end snapshot for trend analysis (TR-004) |
| Action items | Indefinite (GH issue) | Owned by GH/squad-message fallback |
| Audit log (raw_post.read) | 90d | Object-lock; immutable |
| Vote records | 30d aggregated, then individual votes purged | Same job as raw posts |

Purge job MUST: (a) overwrite ciphertext with zero-bytes before delete (defense vs. tombstone-recovery in some stores), (b) emit `raw_post.purged{post_id, ts, reason}` audit event, (c) be re-runnable safely (idempotent on already-purged rows).

### 3.3 Key management

- **Prod:** Master key in cloud KMS-equivalent. Data-key envelope per retro_id (limits blast radius).
- **Dev/local-Aspire:** `RETRO_RAW_KEY` env var, 32-byte base64. AppHost rejects start if unset when retro module is enabled. Documented in `appsettings.example.json`.
- **Rotation:** Master key rotation re-wraps data keys in background; in-flight reads tolerate both wrap versions. Key version embedded in `encrypted_data_key` prefix byte.

---

## 4. Audit log schema

```json
{
  "event": "raw_post.read",
  "ts": "2026-06-23T10:42:00Z",
  "retro_id": "retro_2026-06-15_sprint42",
  "post_id": "post_01HXYZ...",
  "viewer_id": "user_alice@contoso.com",
  "viewer_role": "facilitator",
  "reason_code": "transcript_review",
  "prev_hash": "sha256:abcd...",
  "self_hash": "sha256:efgh..."
}
```

Events emitted:
- `raw_post.created` — ingestion
- `raw_post.read` — facilitator view; author self-read is NOT audited (reduces noise; author already knows)
- `raw_post.purged` — retention expiry
- `raw_post.access_denied` — authz failure (security signal)
- `key.rotated` — master-key rotation
- `aggregate.rendered` — transcript share (links to retro_id; no per-post detail)

Sink: OTel log signal with attribute `audit.category=retro_privacy`. Filtered into separate sink with object-lock in prod.

---

## 5. Enforcement (CI gates)

A new semgrep file ships with this doc: `.semgrep/retro-privacy.yml` (sibling to existing `squad-spawn-rules.yml`, `auth-ui-token-hygiene.yml`). Rules:

| Rule ID | Severity | Pattern |
|---|---|---|
| `no-raw-post-in-transcript` | ERROR | Calls to `Transcript.Render(...)` / equivalent must NOT take `RawPost` or `string` typed as raw post. Enforces type-level separation. |
| `no-raw-post-in-log` | ERROR | `logger.{Info,Warn,Error,Debug}(...{RawPost}...)` or string interpolation of `.raw_text`/`.RawText` properties. |
| `no-raw-post-in-otel-attr` | ERROR | `activity.SetTag` / `span.SetAttribute` with values matching `raw_text`, `*.body`, `post.text`. |
| `no-raw-post-in-issue-body` | ERROR | `GitHubClient.CreateIssue` body parameter must come from `ActionItem.Body`, never from `RawPost.*`. |
| `require-aad-on-raw-encrypt` | ERROR | `AesGcm.Encrypt(...)` call on raw-post path missing `associatedData` argument. |
| `no-bulk-raw-export` | ERROR | Methods returning `IEnumerable<RawPost>` / `RawPost[]` outside reducer internals. |

Path scope: `**/retro/**`, `**/Retro/**`, `src/AspireWithSquad.Retro*/**`.

---

## 6. Open contract decisions (need ack from app-dev + IRP)

| ID | Question | Recommendation |
|---|---|---|
| **D5-1** | Confirm 30d retention (vs IRP D5 default) | **Confirm**, with stricter `min(30d, retro_close+7d)` floor. |
| **D5-2** | Author self-read audit? | **No** — author always has access to own posts; auditing creates noise. |
| **D5-3** | Min cohort size for sentiment category in transcript | **2.** Singletons collapse to "other" to prevent de-anonymization. |
| **D5-4** | LLM-generated action items: pass raw or aggregate? | **Aggregate only.** TR-003 synthesizer never sees raw text. |
| **D5-5** | Facilitator can export raw posts to file? | **No.** Read-in-UI only; no bulk export path. Bypasses are CI-blocked (rule `no-bulk-raw-export`). |

---

## 7. Sign-off checklist (for TR-008 E2E privacy assertions)

QT property tests MUST cover:

- [ ] `Transcript.Render` output never contains any `RawPost.raw_text` substring (Jaccard ≤0.3 vs each raw post).
- [ ] After purge job runs, `RawPostStore.Get(retro_id)` returns 0 rows AND ciphertext blob in underlying store is zero-bytes.
- [ ] `raw_post.read` audit event emitted exactly once per facilitator view; not emitted on author self-read.
- [ ] Singleton sentiment category collapsed to "other" when cohort size = 1.
- [ ] Action-item body Jaccard vs any raw post ≤0.5 (synthesizer never echoes raw).
- [ ] Cross-retro read attempt (mismatched `retro_id` in facilitator claim) returns 403 + emits `access_denied`.
- [ ] Key rotation: posts encrypted with old key still decrypt after rotation; new posts use new key.
- [ ] Audit log hash chain: tamper with one entry → verifier detects break.

---

## 8. Out-of-scope for v1

- Differential privacy on aggregates (defer to v2 if cohort >50)
- Right-to-erasure pre-30d (defer; current retention floor is already short)
- Cross-tenant retro sharing (no tenant boundary in v1)
- Sentiment ML model — using rule-based theme extraction in v1; ML privacy review needed before ML lands.

---

**Implementation handoff:** application-development-squad owns reducer + store + purge job. security-hardening-squad owns `.semgrep/retro-privacy.yml` (ships with this commit) and reviews TR-001/003 PRs against this doc. quality-testing-squad owns §7 assertions in TR-008.
