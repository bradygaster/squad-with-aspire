# Rate-limit response contract (429)

**Owners:** experience-design-squad (UI countdown) + security-hardening-squad (enforcement) + application-development-squad (implementation).
**Applies to:** all auth endpoints — `/api/auth/login`, `/api/auth/register`, `/api/auth/verify/resend`, and any future auth-adjacent endpoint that enforces a rate limit.

This file resolves the "429 response shape" open question in `register.md`.

---

## Wire format

When a request is rate-limited, the API returns:

```
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 47
Cache-Control: no-store

{
  "code": "RATE_LIMITED",
  "message": "Too many attempts. Try again in 47 seconds.",
  "retryAfterSeconds": 47,
  "scope": "ip" | "account" | "global"
}
```

### Field semantics

| Field | Required | Source of truth | Notes |
|---|---|---|---|
| `Retry-After` header | yes | server | seconds (integer). Standard HTTP; CDNs/proxies may honor it. |
| `code` | yes | server | always literal `"RATE_LIMITED"` for this class. UIs key off this. |
| `message` | yes | server | localized, human-readable. UI **may** display verbatim; **must not** parse. |
| `retryAfterSeconds` | yes | server | integer ≥ 1. **This is what the UI countdown uses.** Duplicates `Retry-After` for clients that can't read headers (e.g. fetch in some sandboxed contexts). |
| `scope` | yes | server | tells the UI whether the limit is per-IP, per-account, or global. Drives copy variants (see below). |

### Why both header and body
- `Retry-After` is the standard, but `fetch()` in browsers does not always expose all response headers when CORS is involved without explicit `Access-Control-Expose-Headers`. Body field guarantees the UI has it.
- The two MUST agree. If they don't, the UI trusts `retryAfterSeconds` (body).

---

## UI behavior contract (experience-design-squad owns)

1. **Render countdown from `retryAfterSeconds`.** Decrement every 1s client-side. Do **not** poll the server.
2. **Announce via `aria-live=polite`** on a region near the action. Throttle announcements to thresholds: initial value, 30s, 10s, 0s. Continuous announcements are SR spam.
3. **Disable the triggering control** for the duration using `aria-disabled=true` (not the `disabled` attribute — keeps it focusable so SR can re-read the state).
4. **At 0**, re-enable the control and clear the live region. Do not auto-retry.
5. **Copy varies by `scope`**:
   - `ip`: "Too many attempts from this network. Try again in Ns."
   - `account`: "Too many attempts for this account. Try again in Ns." (login/resend)
   - `global`: "Service is throttled. Try again in Ns." (rare; treat as transient)
6. **Never reveal whether the email exists** in the message. `scope: "account"` is safe because the user already entered the email — it tells them nothing new.

---

## Enforcement contract (security-hardening-squad owns)

1. Token bucket or sliding window — implementation choice. Limits are **per endpoint**, not global.
2. Suggested defaults (security squad to confirm and document in their own spec):
   - `/api/auth/login` — 10 per 15min per IP, 5 per 15min per account.
   - `/api/auth/register` — 5 per hour per IP.
   - `/api/auth/verify/resend` — 1 per 60s per account (cooldown), 5 per hour per account (hard cap).
3. `retryAfterSeconds` must reflect the **soonest** time the next attempt would succeed. Never return a value > 3600 — cap and surface as "Try again later" copy variant if the underlying window is longer.
4. On account-scope limits, do not differentiate "no such account" from "wrong password" in either status code or timing (constant-time comparison).

---

## Test cases (handoff to quality-testing-squad)

| Case | Endpoint | Expected response | UI assertion |
|---|---|---|---|
| 11th login in 15min, same IP | `/api/auth/login` | 429, scope=ip, retryAfterSeconds≥1 | error region populated, countdown visible, submit `aria-disabled=true` |
| 6th resend in 1hr, same account | `/api/auth/verify/resend` | 429, scope=account | resend button stays in cooldown, copy mentions "this account" |
| Countdown reaches 0 | n/a | n/a | submit re-enables, live region cleared, can retry |
| Body and header disagree | n/a | n/a | UI uses body value (`retryAfterSeconds`), test must inspect rendered countdown |
| `retryAfterSeconds: 0` or negative | n/a | server bug — never send | UI clamps to 1s minimum and logs (don't crash) |

---

## OpenAPI snippet (for app-dev to drop in)

```yaml
components:
  schemas:
    RateLimited:
      type: object
      required: [code, message, retryAfterSeconds, scope]
      properties:
        code:
          type: string
          enum: [RATE_LIMITED]
        message:
          type: string
        retryAfterSeconds:
          type: integer
          minimum: 1
          maximum: 3600
        scope:
          type: string
          enum: [ip, account, global]
  responses:
    RateLimited:
      description: Rate limit exceeded
      headers:
        Retry-After:
          schema: { type: integer, minimum: 1 }
      content:
        application/json:
          schema: { $ref: '#/components/schemas/RateLimited' }
```

Reference in any auth endpoint:
```yaml
responses:
  '429':
    $ref: '#/components/responses/RateLimited'
```
