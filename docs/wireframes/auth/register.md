# Register Screen Wireframe

**API contract:** `POST /api/auth/register` → `{ email, password }` → `201 { token, user }` | 409 email-taken | 400 validation (owner: application-development-squad)

**Route:** `/register` · **Title:** "Create account"

---

## Desktop (1280px)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  [Logo]                                                          Help  EN ▾   │
├──────────────────────────────────────────────────────────────────────────────┤
│                          ┌────────────────────────────┐                      │
│                          │     Create account         │  h1                  │
│                          │                            │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ ⚠ Error region         │ │  role=alert          │
│                          │ └────────────────────────┘ │                      │
│                          │                            │                      │
│                          │ Email                      │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ you@example.com        │ │  autocomplete=username │
│                          │ └────────────────────────┘ │                      │
│                          │                            │                      │
│                          │ Password         [Show 👁] │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ ••••••••               │ │  autocomplete=new-password │
│                          │ └────────────────────────┘ │  aria-describedby=pw-hint pw-strength │
│                          │ ▓▓▓▓░░░░░░░░  Weak         │  #pw-strength aria-live=polite │
│                          │ Use 12+ chars, mix of      │  #pw-hint            │
│                          │ letters, numbers, symbols. │                      │
│                          │                            │                      │
│                          │ Confirm password [Show 👁] │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ ••••••••               │ │  autocomplete=new-password │
│                          │ └────────────────────────┘ │                      │
│                          │                            │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │    Create account      │ │  primary, disabled   │
│                          │ └────────────────────────┘ │  until form valid    │
│                          │                            │                      │
│                          │      ← Back to sign in     │  <a href="/login">   │
│                          └────────────────────────────┘                      │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Mobile (375px)

```
┌──────────────────────────┐
│ [Logo]            ☰      │
├──────────────────────────┤
│  Create account          │
│                          │
│ ┌──────────────────────┐ │
│ │ ⚠ error region       │ │
│ └──────────────────────┘ │
│                          │
│ Email                    │
│ ┌──────────────────────┐ │
│ └──────────────────────┘ │
│                          │
│ Password      [Show 👁]  │
│ ┌──────────────────────┐ │
│ └──────────────────────┘ │
│ ▓▓▓▓░░░░░░  Weak         │
│ 12+ chars, mix letters/  │
│ numbers/symbols.         │
│                          │
│ Confirm       [Show 👁]  │
│ ┌──────────────────────┐ │
│ └──────────────────────┘ │
│                          │
│ ┌──────────────────────┐ │
│ │   Create account     │ │
│ └──────────────────────┘ │
│                          │
│    ← Back to sign in     │
└──────────────────────────┘
```

---

## Password strength meter
- 4 segments. Score derived from length + character classes (deterministic client-side; final policy enforced by API).
- Labels: 0–1 "Too weak" (red #CF222E), 2 "Weak" (orange #BC4C00), 3 "Good" (yellow #9A6700), 4 "Strong" (green #1A7F37). All ≥ 4.5:1 on white.
- `<div role=progressbar aria-valuenow aria-valuemin=0 aria-valuemax=4 aria-labelledby=pw-strength-label>`.
- Strength label in `aria-live=polite` (announces only on tier change, debounced 400ms).
- Submit stays disabled until score ≥ 2 AND confirm matches.

## States

### Empty
- Strength meter at 0, label "Enter a password".
- Submit disabled, `aria-disabled=true` (still focusable so SR users can read state).

### Typing
- Strength updates live (debounced).
- Confirm field shows ✓ when match, ✗ "Passwords don't match" when not (only after blur or after first mismatch correction).

### Loading
- Same as login: button label "Creating account…", `aria-busy`, inputs `readonly`.

### Error
- 409 email-taken: `#form-error` "An account with that email already exists. [Sign in instead]" (the bracketed text is a link to `/login?email=...`).
- 400 validation: per-field, `aria-invalid`, focus to first invalid.
- 429 rate-limit: "Too many attempts. Try again in N seconds." (countdown updates `aria-live=polite`).

### Success
- 201 → store token → redirect to `/welcome` (or returnTo).

---

## Focus order (Tab)
1. Skip-to-content
2. Logo
3. Header links
4. Email
5. Password
6. Password show/hide
7. Confirm password
8. Confirm show/hide
9. Create account button
10. Back to sign in

## Accessibility checklist
- Password hint text wired via `aria-describedby` so SR reads requirements with the field.
- Strength meter is decorative-plus-progressbar; text label is the source of truth.
- Show/hide toggles are independent per field, each `aria-pressed`.
- Confirm mismatch error: `<p id=confirm-err role=alert>` referenced by `aria-describedby`.
- No password constraints communicated only via color — always pair with text.
- Contrast: see login.md (same tokens).
- Form does NOT auto-submit on Enter from any field except the last; pressing Enter elsewhere advances focus (or submits if all valid) — standard browser behavior, do not intercept.

---

## Open questions for application-development-squad
1. ~~Confirm `POST /api/auth/register` returns `{ token, user }` on 201 (same shape as login)?~~ **Resolved in `verify-email.md`:** API returns `{ token?, user, requiresVerification }`. Token issued iff `requiresVerification: false`.
2. ~~Minimum password policy enforced server-side?~~ **Resolved:** API is source of truth via `400 { code: "WEAK_PASSWORD" }`. Client meter is hint-only. App-dev to document in OpenAPI.
3. ~~Email verification flow~~ **Resolved:** see `verify-email.md`. API `requiresVerification` flag drives redirect to `/verify-email` vs `/welcome`.

## Open question for security-hardening-squad
- ~~Rate-limit response shape for 429~~ **Resolved in `rate-limit-contract.md`:** `{ code: "RATE_LIMITED", message, retryAfterSeconds, scope }` + `Retry-After` header. Both files locked, no further design input required.
