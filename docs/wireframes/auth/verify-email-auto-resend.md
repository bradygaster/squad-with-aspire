# Verify-Email Auto-Resend UX Spec

**Status:** Locked. Closes QT outstanding item from commit `17c82ac`.
**Scope:** Wiring contract for `reduceVerifyEmail()` consumers (`packages/auth-ui-contracts/`).
**Owners:** experience-design (this spec) → application-development (implementation).

## 1. When auto-resend fires

Auto-resend fires **once and only once** per page load when ALL of these are true:

1. Initial verify call returns `400 { code: "TOKEN_INVALID" }` **OR** `400 { code: "TOKEN_EXPIRED" }`.
2. The page was reached via email link (URL contains `?token=...`) — NOT via manual navigation to `/verify-email`.
3. The user has NOT previously clicked Resend on this page load (tracked via reducer's `resendAttempted` flag — add to state if not present).
4. We have a recoverable email context: either a session cookie identifying the unverified account, OR a `?email=` query param echoed from the registration redirect.

If condition 4 fails, do NOT auto-resend. Show the manual Resend button instead with copy: *"This link can't be used. Enter your email to get a new one."* (adds an email input above the Resend button.)

## 2. Why "once and only once"

- Prevents resend-loop on a permanently broken token (e.g., user forwarded the email to a colleague who clicked twice).
- Prevents accidental rate-limit trip on the auto path before the user has any agency.
- Server-side `5/hr account` cap from `rate-limit-contract.md` is the backstop, but client-side single-shot is the contract.

## 3. State machine wiring

```
verifyPending
  ├─ 200 → verifySuccess
  ├─ 400 TOKEN_INVALID + canAutoResend → resendPending (auto) → ...
  ├─ 400 TOKEN_EXPIRED + canAutoResend → resendPending (auto) → ...
  ├─ 400 + !canAutoResend → tokenInvalidOrExpired (manual Resend visible)
  └─ 410 TOKEN_USED → tokenUsed (no Resend — already verified)
```

After auto-resend completes:

- `202 → resendSuccess` — show success banner: *"We sent a new verification link to {emailMasked}. Check your inbox."* Mask as `j***@example.com`.
- `429 → resendRateLimited` — show throttled copy from `rate-limit-contract.md`, account scope. The auto-attempt counts against the budget; honest UX.
- Network/5xx → `tokenInvalidOrExpired` with manual Resend button (do NOT silently retry; user must take action).

## 4. Telemetry events

Emit on the wiring layer (not in the pure reducer):

| Event | When | Payload |
|---|---|---|
| `auth.verify.token_invalid` | 400 received | `{ code: "TOKEN_INVALID" \| "TOKEN_EXPIRED", autoResendEligible: bool }` |
| `auth.verify.auto_resend_fired` | Auto-resend HTTP request sent | `{ trigger: "token_invalid" \| "token_expired" }` |
| `auth.verify.auto_resend_result` | After response | `{ outcome: "success" \| "rate_limited" \| "error", httpStatus }` |

`auto_resend_fired` and `auto_resend_result` MUST come in pairs — alert if `_fired` count > `_result` count over 5min window (indicates client crashes mid-flight).

## 5. Accessibility

When auto-resend transitions states without user input, the live region MUST announce the transition (otherwise SR users see the heading change but don't know why):

- On `verifyPending → resendPending (auto)`: announce *"Verification link expired. Sending you a new one."* via `aria-live=polite`.
- On `resendPending → resendSuccess`: announce success copy (existing behavior).
- On `resendPending → resendRateLimited`: announce rate-limit copy (existing behavior, threshold-throttled per `shouldAnnounce()`).

The heading-focus contract from `headingFocusAttrs()` still applies: focus moves to `h1` on terminal states (`resendSuccess`, `resendRateLimited`, `tokenInvalidOrExpired`, `tokenUsed`). Do NOT move focus during the intermediate auto-resend pending state — that traps SR users mid-transition.

## 6. Non-goals

- No retry-on-network-error. One auto-attempt, then manual.
- No silent retry on 429. The 429 surfaces immediately with countdown.
- No auto-resend on `/verify-email` reached without a token (user-initiated resend page).
- No client-side rate-limit beyond "once per page load". Server is the enforcement layer.

## 7. Test hooks (for QT's DOM harness when it lands)

1. Mount `<VerifyEmail>` with URL `?token=expired-fixture` → assert one POST to `/api/auth/verify/resend` fires within 500ms.
2. Mount with `?token=expired-fixture` twice (simulate React StrictMode double-mount) → assert exactly ONE POST fires (idempotency guard).
3. Mount with `?token=expired-fixture`, then user clicks Resend → assert second POST does NOT fire from auto path (only manual).
4. Mount without `?token=` and no session → assert no auto-resend, manual Resend button visible.
5. Auto-resend returns 429 → assert focus lands on h1 with rate-limit copy + countdown announced.

## 8. Open follow-up (not a blocker)

When the wiring layer adds `resendAttempted` to the reducer state, the existing reducer-purity test (QT case in `auth-verify-email-states.test.ts`) must be extended: assert `resendAttempted` flips to `true` exactly once across the auto-flow event sequence, and stale `RESEND_CLICKED` events are no-ops once it's true.
