# Auth — Service Unavailable (503) UX Spec

Addendum to `rate-limit-contract.md`. Covers UI behavior when the rate-limit
backing store (Redis) is unreachable and auth endpoints fail-closed with 503.

Triggered by: `POST /api/auth/login`, `/register`, `/forgot-password`,
`/reset-password`, `/verify/resend` returning HTTP 503 with body
`{ code: "AUTH_UNAVAILABLE", message, retryAfterSeconds? }`.

Distinct from 429 (rate-limited): 503 is a server-side dependency outage, not
the user's fault. Copy must not blame the user or imply they did something wrong.

## 1. Response shape (contract with security + app-dev)

```json
{
  "code": "AUTH_UNAVAILABLE",
  "message": "Authentication is temporarily unavailable.",
  "retryAfterSeconds": 30
}
```

- `retryAfterSeconds` is OPTIONAL. When omitted, UI shows the generic variant
  (no countdown). When present, UI shows the countdown variant. Server SHOULD
  send a short hint (10–60s) to encourage spaced retries; UI clamps to
  `[5, 300]` to bound countdown length.
- `Retry-After` header SHOULD match body if both present. Body wins.
- Code is stable: `AUTH_UNAVAILABLE`. Do not overload `RATE_LIMITED`.

## 2. Copy matrix

| Surface       | Headline                                | Body                                                                 |
|---------------|------------------------------------------|----------------------------------------------------------------------|
| Generic 503   | Sign-in is temporarily unavailable      | We can't sign you in right now. Try again in a moment.               |
| With countdown| Sign-in is temporarily unavailable      | We can't sign you in right now. Try again in **{n} seconds**.        |
| Register 503  | Account creation is temporarily unavailable | We can't create accounts right now. Try again in a moment.       |
| Resend 503    | Resend is temporarily unavailable       | We can't resend the email right now. Try again in a moment.          |
| Forgot 503    | Password reset is temporarily unavailable | We can't send reset emails right now. Try again in a moment.       |

Tone rules:
- No "Oops" / "Whoops" / "Sorry for the inconvenience".
- No mention of Redis, cache, backend, downstream, infrastructure.
- No "please" (matches voice matrix from #1372).
- Never include the literal string `503` in user-facing copy.

## 3. Component behavior

Reuse the `<AuthFormBanner variant="error">` component from `login.md` §4.
503 is a transient error variant, distinct from `variant="rate-limit"`:

- `role="alert"` + `aria-live="assertive"` on first render (mount-on-error
  pattern from `login.md`).
- Form fields stay **enabled** (user keeps their typed input — refresh would
  lose it on register).
- Submit button is **disabled** with `aria-disabled="true"` while the countdown
  is active. Label stays "Sign in" / "Create account" — do NOT change to
  "Unavailable" (screen readers re-announce on label change).
- When `retryAfterSeconds` absent: submit re-enables after 5s and the banner
  stays until the next submit attempt.
- When `retryAfterSeconds` present: countdown ticks down once per second,
  banner text updates via `aria-live="polite"` throttled to 30/10/0s
  thresholds (same throttle pattern as 429 — see `rate-limit-contract.md` §4).
- On retry success, banner dismisses with `aria-live="polite"` "Signed in."
  announcement.

## 4. Spinner floor interaction

Confirms `rate-limit-enforcement.md` §5.2: server enforces 250ms ± 50ms response
floor on login/forgot/resend. Client `aria-busy="true"` toggle MUST remain set
for **≥300ms** even when the response comes back faster (e.g., from a CDN edge
503 before reaching the rate-limit middleware). Use `Math.max(elapsed, 300)`
before clearing `aria-busy`. Prevents screen-reader spinner-announcement flicker.

## 5. What this is NOT

- Not 429. 429 = "you specifically hit a limit." 503 = "the service can't
  decide right now."
- Not the global 5xx toast. Auth surfaces use the in-form banner; toast is
  reserved for non-form contexts (account settings, etc.).
- Not retried automatically. No auto-retry, no exponential backoff in the
  client. User-initiated retry only — preserves their typed input and avoids
  thundering-herd on Redis recovery.
- Not CAPTCHA fallback. CAPTCHA is reserved for circuit-breaker trip per
  security squad; that path opens a different wireframe.

## 6. Telemetry hooks

Emit client event `auth.unavailable.shown` with `{ endpoint, retryAfterSeconds,
hadCountdown }` on banner mount. Distinct from `auth.rate_limited.shown`.
Lets us tell "users seeing 503" from "users seeing 429" in dashboards without
parsing copy.

## 7. Open items (none blocking)

- Designer follow-up: illustration for full-page 503 (only relevant if a future
  flow renders 503 outside a form, e.g., magic-link landing). Out of scope for
  v1.
- i18n: copy strings keyed under `auth.errors.unavailable.*` namespace (parallel
  to `auth.errors.rateLimited.*`). Resolver lands with i18n rollout.
