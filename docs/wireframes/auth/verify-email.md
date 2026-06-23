# Verify Email Screen Wireframe

**API contract:** `GET /api/auth/verify?token=...` → `200 { verified: true, user }` | `400 { code: "TOKEN_INVALID" | "TOKEN_EXPIRED" }` | `410 { code: "TOKEN_USED" }`
**Resend:** `POST /api/auth/verify/resend` → `202 { cooldownSeconds: 60 }` | `429 { code: "RATE_LIMITED", retryAfterSeconds }`

**Route:** `/verify-email` · **Title:** "Verify your email"
**Owner:** experience-design-squad. **Depends on:** application-development-squad (endpoints), security-hardening-squad (rate-limit shape — see `rate-limit-contract.md`).

---

## Decision: post-register routing

After successful `POST /api/auth/register` (201), the API response carries `requiresVerification: boolean`:

- `requiresVerification: true` → redirect to `/verify-email?email=...` (this screen). **No token issued yet.**
- `requiresVerification: false` → store token, redirect to `/welcome` (current default for dev / no-SMTP environments).

This keeps the same client redirect helper for both modes; the API owns the toggle. Resolves open question #3 in `register.md`.

---

## Desktop (1280px)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  [Logo]                                                          Help  EN ▾   │
├──────────────────────────────────────────────────────────────────────────────┤
│                          ┌────────────────────────────┐                      │
│                          │       📧 (decorative)      │  aria-hidden=true    │
│                          │                            │                      │
│                          │   Check your email         │  h1                  │
│                          │                            │                      │
│                          │   We sent a verification   │  <p>                 │
│                          │   link to                  │                      │
│                          │   <b>you@example.com</b>   │  email from query    │
│                          │                            │                      │
│                          │   Click the link to        │                      │
│                          │   finish creating your     │                      │
│                          │   account.                 │                      │
│                          │                            │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ ⚠ Error region         │ │  role=alert, hidden  │
│                          │ └────────────────────────┘ │  until populated     │
│                          │                            │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │  Resend email          │ │  secondary button    │
│                          │ └────────────────────────┘ │                      │
│                          │   Resend available in 47s  │  cooldown, aria-live │
│                          │                            │                      │
│                          │   Wrong address?           │                      │
│                          │   ← Use a different email  │  <a href="/register">│
│                          │                            │                      │
│                          │   Already verified?        │                      │
│                          │   → Sign in                │  <a href="/login">   │
│                          └────────────────────────────┘                      │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Mobile (375px)

Same content, full-bleed card, 16px gutter, 48px tall buttons. Email address wraps on its own line.

---

## States

### Initial (just arrived from /register)
- Resend button enabled. No cooldown shown.
- Focus lands on the h1 (programmatic, `tabindex=-1`) so SR users hear the page title immediately. Screen reader announces full instruction paragraph as part of the heading region.

### Resend pending
- Button label "Resending…", `aria-busy=true` on the button only (form-level busy not appropriate here — no form).
- Disable button via `aria-disabled=true` + `pointer-events:none`; do **not** use `disabled` attribute (so SR still reads label).

### Resend success (202)
- Toast/inline message: "New link sent to you@example.com." (`role=status`, `aria-live=polite`).
- Cooldown starts: button shows "Resend available in Ns" countdown, decrementing every 1s. At 0 the button re-enables and the text clears.
- Cooldown source-of-truth: `cooldownSeconds` from API response — never compute client-side from previous click time.

### Resend rate-limited (429)
- Error region: "Too many resend attempts. Try again in N seconds."
- Countdown driven by `retryAfterSeconds` from response body (see `rate-limit-contract.md` for shape).
- Button stays in cooldown state for the full duration.

### Link click → verification success (200)
- Brief loading splash (≤500ms target), then redirect to `/welcome`.
- If user arrives at this screen with `?token=...` already in URL, auto-submit verification on mount; show "Verifying…" with `aria-busy=true` on `<main>`.

### Link click → token invalid/expired (400)
- Error region: "This verification link has expired. We'll send a new one."
- Auto-trigger resend after 1s (no user action needed). Announce result via `aria-live=polite`.

### Link click → token already used (410)
- Error region: "This email is already verified." + primary CTA "Sign in" → `/login?email=...`.

---

## Focus order (Tab)
1. Skip-to-content
2. Logo
3. Header links
4. Resend email button
5. Use a different email link
6. Sign in link

## Accessibility checklist
- Heading order: h1 = "Check your email". No skipped levels.
- Email address rendered in `<b>` (visual emphasis only, no semantic weight) — the full sentence still reads naturally to SR.
- Cooldown countdown: live region update rate ≤ 1/sec; debounce to avoid SR spam. Consider announcing only at 30s/10s/0s thresholds via a separate hidden live region.
- The 📧 icon has `aria-hidden=true`; meaning carried entirely by the h1 + paragraph.
- No CAPTCHA on resend — rate-limit + cooldown carry the burden. If security squad requires CAPTCHA later, add as a separate wireframe; current spec assumes no.

---

## Resolved open questions (formerly in register.md)

1. **`POST /api/auth/register` 201 response shape** — confirmed by app-dev: `{ token?, user, requiresVerification }`. Token present iff `requiresVerification: false`.
2. **Password policy** — app-dev to document in OpenAPI. Client treats `400 { code: "WEAK_PASSWORD", message }` as authoritative; client-side strength meter is hint-only.
3. **Post-register flow** — resolved here: API drives via `requiresVerification`.

## Resolved for security-hardening-squad
- 429 response shape locked in `rate-limit-contract.md` (companion file). Both `/api/auth/register` and `/api/auth/verify/resend` use the same shape.
