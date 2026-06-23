# Auth Rate-Limit Enforcement Spec

**Owner:** security-hardening-squad
**Binding contract:** `docs/wireframes/auth/rate-limit-contract.md` (UX/wire-format)
**Status:** Locked for v1. Changes require security-squad + UX sign-off.

This spec defines server-side enforcement for the 429 contract. The wire format,
copy, and UI behavior are owned by UX. This document covers algorithm, key
derivation, storage, timing-attack defenses, observability, and abuse cases.

---

## 1. Wire format (recap — authoritative source is UX contract)

```
HTTP/1.1 429 Too Many Requests
Retry-After: <seconds, integer, 1..3600>
Content-Type: application/json

{
  "code": "RATE_LIMITED",
  "message": "<scope-specific copy from UX matrix>",
  "retryAfterSeconds": <same as header>,
  "scope": "ip" | "account" | "global"
}
```

Header and body **must agree**. UI trusts body on disagreement; we still emit
both because intermediaries (Cloudflare, App Gateway) honor the header.

---

## 2. Algorithm: sliding-window log + token-bucket cooldown

Two primitives, picked per-endpoint:

### 2.1 Sliding window log (default)
- Redis sorted-set per `(scope, key, endpoint)`. Member = nonce, score = unix ms.
- On request: `ZREMRANGEBYSCORE` older than window, `ZCARD`, then `ZADD` if under
  limit. Pipeline all three; set TTL = window + 60s.
- `retryAfterSeconds = ceil((oldestScoreInWindow + windowMs - now) / 1000)`,
  clamped to `[1, 3600]`.

### 2.2 Cooldown bucket (resend-style endpoints)
- Single key `(scope, key, endpoint, "cooldown")` with `SET NX EX <cooldownSec>`.
- If `NX` fails: 429, `retryAfterSeconds = TTL` of existing key.
- Cooldown is layered on top of a sliding window for hard caps (see `/verify/resend`).

### 2.3 Why not fixed window
Fixed windows allow 2× burst at boundary. Auth abuse tooling exploits this.
Sliding log is O(window-size) memory per key — auth windows are small (≤1hr),
acceptable.

---

## 3. Per-endpoint configuration (v1)

| Endpoint                  | Scope: IP             | Scope: Account              | Cooldown          | Notes |
|---------------------------|-----------------------|-----------------------------|-------------------|-------|
| `POST /api/auth/login`    | 10 / 15min            | 5 / 15min                   | —                 | Account scope keyed by **lowercased email hash**, not user-id (avoid enumeration of which addresses exist as accounts). |
| `POST /api/auth/register` | 5 / 1hr               | —                           | —                 | IP only; account doesn't exist yet. |
| `POST /api/auth/verify/resend` | 20 / 1hr         | 5 / 1hr                     | **60s / account** | Cooldown is the dominant constraint; hard caps catch automation. |
| `POST /api/auth/verify/confirm` | 30 / 1hr        | 10 / 1hr                    | —                 | Token-bound — abuse is brute-force on token, not floods. |
| `POST /api/auth/forgot-password` | 5 / 1hr        | 3 / 1hr                     | 60s / account     | Same shape as resend. |
| `POST /api/auth/reset-password` | 10 / 1hr         | 5 / 1hr                     | —                 | Token-bound. |
| Global circuit-breaker    | 1000 / 1min (all auth)| —                           | —                 | `scope: "global"` triggers maintenance-mode copy. Tripped by ops alert, not auto. |

**Precedence when multiple limits trip simultaneously:** return the **longest**
`retryAfterSeconds`, with scope of that longest limit. Rationale: UX copy is
scope-specific; surfacing the binding constraint helps the user help themselves.

---

## 4. Key derivation

### 4.1 IP scope
- Trust `X-Forwarded-For` **only** when behind known reverse proxy (Front Door
  or App Gateway). Take **leftmost untrusted hop** per
  [RFC 7239 §5.2](https://www.rfc-editor.org/rfc/rfc7239).
- IPv4: full address. IPv6: `/64` prefix (per-host normalization; per-address
  trivially evaded with SLAAC).
- Hash with HMAC-SHA256 keyed by `RATE_LIMIT_HMAC_KEY` before storing — Redis
  keyspace must not leak source IPs to ops with KEYS access.

### 4.2 Account scope
- Key = `HMAC-SHA256(RATE_LIMIT_HMAC_KEY, lowercase(trim(email)))`.
- **Never** key by user-id discovered via DB lookup; that lookup itself is the
  enumeration oracle (§5).
- Email normalization: lowercase + trim only. Do **not** strip `+tags` or dots —
  treat as opaque per RFC 5321 local-part semantics for limiter purposes.

### 4.3 Global scope
- Single fixed key. Counter only; no derivation.

---

## 5. Timing-attack & enumeration defenses

The biggest leak risk on auth endpoints is **account existence** via
differential response time or 429 timing. Required mitigations:

1. **Constant-time account-scope check.** Always run the account-scope
   Redis ops on every request, even when the email doesn't match an account.
   Use HMAC of normalized email as the key so the existence question is never
   asked of the DB before rate-limit decision.

2. **Response time floor.** Wrap login/register/forgot/resend handlers with a
   minimum-duration guard (250ms ± 50ms jitter). Implementation pattern:
   ```csharp
   var floor = TimeSpan.FromMilliseconds(250 + Random.Shared.Next(0, 50));
   var sw = Stopwatch.StartNew();
   try { return await handler(); }
   finally {
       var remaining = floor - sw.Elapsed;
       if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
   }
   ```

3. **Identical 429 body regardless of account existence.** Account-scope 429
   copy says "this account" not "this email" — UX matrix already enforces this.
   Critically: scope must be **`account`** whether the account exists or not.
   Never return `scope: "ip"` when the email matched and `scope: "account"`
   when it didn't.

4. **No `Vary: Authorization` differentiation on 429 cache headers.** All
   429s carry `Cache-Control: no-store`.

---

## 6. Storage & failure modes

### 6.1 Primary store: Redis
- Connection pool sized at `2 × CPU` per app instance, max 50.
- Pipelined ops only; round-trip count = 1 per limit check.
- Required commands: `ZADD`, `ZCARD`, `ZREMRANGEBYSCORE`, `SET NX EX`, `TTL`,
  `EXPIRE`. Cluster-safe (all keys for one decision share a hash-tag).

### 6.2 Fail-open vs fail-closed
**Fail-closed** for auth endpoints when Redis is unavailable:
- Login/register/reset: return **503 Service Unavailable** with
  `Retry-After: 30`. **Do not** silently allow requests — losing the rate
  limiter on these endpoints is a credential-stuffing window.
- Resend: fail-closed (return 503).
- Token confirm: fail-**open** with audit log entry. Confirm is bounded by the
  token itself (single-use, time-limited); rate-limit is defense-in-depth.

**Implementation gate:** an `IRateLimitStore` interface with `Redis`,
`InMemory` (dev/test only), and `FailClosed` decorators. Configurable per
endpoint via `RateLimitOptions:FailureMode = "Closed" | "Open" | "Closed503"`.

### 6.3 Memory bound
Worst case per key: `limit` entries × ~50 bytes = ~500 bytes for a 10-req
window. At 1M unique IPs/hour: ~500MB. Acceptable. TTL guarantees eviction.

---

## 7. Bypass list & service accounts

- Internal health checks: bypass via `X-Internal-Probe: <HMAC(timestamp)>`
  header validated by middleware. Probes are exempt from limits but counted in
  a separate metric.
- Penetration test / red team: time-boxed IP allow-list pushed via config flag,
  expires automatically after 8 hours, audited.
- **No bypass for user accounts.** No "trusted user" tier. Defense-in-depth
  requires the limiter to be uniform.

---

## 8. Observability

Required metrics (Prometheus / App Insights):

| Metric | Type | Labels |
|---|---|---|
| `auth_rate_limit_decisions_total` | counter | `endpoint`, `scope`, `decision={allow,deny}` |
| `auth_rate_limit_remaining` | histogram | `endpoint`, `scope` |
| `auth_rate_limit_store_latency_ms` | histogram | `op={read,write}` |
| `auth_rate_limit_store_errors_total` | counter | `kind={timeout,conn,other}` |
| `auth_rate_limit_failopen_total` | counter | `endpoint` — **alert if non-zero** |

**Required alerts:**
- `failopen_total > 0 for 5min` → page on-call.
- `deny rate > 30%` sustained 10min on login → potential credential-stuffing
  attack; trigger global circuit-breaker review.
- `store_latency_ms p99 > 50ms` → degraded; pre-failure indicator.

**Log on deny:** structured event with `endpoint`, `scope`, `keyHash` (first 8
chars of HMAC), `limit`, `window`, `userAgent`. **Never log the raw IP, email,
or full key.** Sampling: 100% deny, 0.1% allow.

---

## 9. Test matrix (security-side)

Extends UX's UI matrix. Mandatory cases:

1. **Burst-at-boundary.** 10 logins at t=14:59, 10 more at t=15:01 — second
   burst must 429 if sliding window correct (would pass with fixed window).
2. **IPv6 host-prefix.** Two `/128` from same `/64` count as same IP-scope key.
3. **X-Forwarded-For spoofing.** Direct request with forged XFF behind proxy
   uses *real* connection IP, not XFF leftmost. Behind trusted proxy: XFF
   leftmost. Test both topologies.
4. **Account enumeration timing.** 100 logins against existing account vs
   nonexistent — response time distributions must be statistically
   indistinguishable (Kolmogorov-Smirnov p > 0.05).
5. **Account enumeration via 429 scope.** When account-scope limit trips on a
   nonexistent email, response carries `scope: "account"`, not `scope: "ip"`.
6. **Redis outage = 503.** With Redis paused, login returns 503 + `Retry-After`,
   *not* 200 (would be silent fail-open).
7. **Cooldown atomicity.** 100 concurrent resend requests for same account →
   exactly 1 success, 99 × 429 with `retryAfterSeconds` in `[59, 60]`.
8. **retryAfterSeconds clamp.** Configured window of 4 hours → response
   reports `3600` max (matches UX contract §"try again later" copy).
9. **Precedence.** Trip IP limit (retry=900s) AND account limit (retry=300s)
   simultaneously → response has scope=ip, retry=900 (longest wins).
10. **Hash-key opacity.** `redis-cli KEYS '*'` output contains no plaintext
    IPs, emails, or user-ids.

Owner of execution: quality-testing-squad. Fixtures live alongside their
existing rate-limit matrix in `docs/wireframes/auth/rate-limit-contract.md`.

---

## 10. Implementation phasing

**P0 (v1 launch — must ship):**
- Sliding-window log on login, register, resend, forgot.
- Cooldown bucket on resend, forgot.
- HMAC key derivation for IP and account.
- Fail-closed-503 on store outage for login/register/forgot/resend.
- 250ms ± 50ms response floor on login/forgot/resend.
- Metrics 1, 2, 5 from §8.

**P1 (post-launch, ≤30 days):**
- Token-confirm and reset-password limits.
- Full metric suite + alerts.
- Global circuit-breaker config plumbing.
- KS-test enumeration tests in CI (statistical, run nightly not per-PR).

**P2 (deferred to v2 unless abuse observed):**
- CAPTCHA fallback. Hook is the global circuit-breaker scope; UX has
  acknowledged they will spec wireframes if requested.
- Adaptive per-user limits based on account-age + 2FA-enrolled signal.

---

## 11. Open questions for application-development-squad

None blocking. For your awareness:

1. We are **not** specifying middleware ordering vs auth/CORS/logging in this
   doc — that's app-dev's lane. Constraint: rate-limit must run **before**
   anything that touches the user record (DB lookup is itself an oracle, §5).
2. `IRateLimitStore` interface shape is suggestive, not prescriptive. If you
   want a different abstraction, fine — preserve the fail-closed-by-default
   semantics and per-endpoint config.
3. `RATE_LIMIT_HMAC_KEY` rotation: out of scope here. Treat like any other
   server-side HMAC secret in the existing key-management pattern.

---

## 12. References

- UX contract: [`docs/wireframes/auth/rate-limit-contract.md`](../../wireframes/auth/rate-limit-contract.md)
- Verify-email wireframe: [`docs/wireframes/auth/verify-email.md`](../../wireframes/auth/verify-email.md)
- OWASP ASVS v4 §11.1 (Business Logic Security), §2.2 (General Authenticator)
- RFC 6585 §4 (429 Too Many Requests)
- RFC 7239 §5.2 (Forwarded for proxies)
