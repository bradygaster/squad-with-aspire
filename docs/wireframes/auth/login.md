# Login Screen Wireframe

**API contract:** `POST /api/auth/login` → `{ email, password }` → `{ token, user }` | 400/401 → `{ error: { code, message } }` (owner: application-development-squad)

**Route:** `/login` · **Title:** "Sign in"

---

## Desktop (1280px)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  [Logo]                                                          Help  EN ▾   │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│                          ┌────────────────────────────┐                      │
│                          │       Sign in              │  h1                  │
│                          │                            │                      │
│                          │ ┌────────────────────────┐ │  ← #form-error       │
│                          │ │ ⚠ Error region (aria-  │ │     role=alert       │
│                          │ │   live=assertive)      │ │     hidden until err │
│                          │ └────────────────────────┘ │                      │
│                          │                            │                      │
│                          │ Email                      │  <label for=email>   │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ you@example.com        │ │  type=email          │
│                          │ └────────────────────────┘ │  required autocomplete=username │
│                          │                            │                      │
│                          │ Password         [Show 👁] │  <label for=pwd> + toggle btn │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │ ••••••••               │ │  type=password       │
│                          │ └────────────────────────┘ │  autocomplete=current-password │
│                          │                            │                      │
│                          │              Forgot password? │  <a href="/forgot"> │
│                          │                            │                      │
│                          │ ┌────────────────────────┐ │                      │
│                          │ │      Sign in           │ │  primary button      │
│                          │ └────────────────────────┘ │  type=submit         │
│                          │                            │                      │
│                          │ ─────────  or  ─────────   │                      │
│                          │                            │                      │
│                          │      Create account →      │  <a href="/register"> │
│                          └────────────────────────────┘                      │
│                          Card: max-w 420px, centered                         │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Mobile (375px)

```
┌──────────────────────────┐
│ [Logo]            ☰      │
├──────────────────────────┤
│                          │
│  Sign in                 │  h1, 24px
│                          │
│ ┌──────────────────────┐ │
│ │ ⚠ error region       │ │  full-width
│ └──────────────────────┘ │
│                          │
│ Email                    │
│ ┌──────────────────────┐ │
│ │                      │ │  44px min height
│ └──────────────────────┘ │
│                          │
│ Password      [Show 👁]  │
│ ┌──────────────────────┐ │
│ │                      │ │
│ └──────────────────────┘ │
│                          │
│        Forgot password?  │
│                          │
│ ┌──────────────────────┐ │
│ │     Sign in          │ │  full-width 48px
│ └──────────────────────┘ │
│                          │
│ ──────  or  ──────       │
│                          │
│    Create account →      │
└──────────────────────────┘
```

---

## States

### Empty (default)
- Error region rendered but `hidden`, retains DOM slot.
- Submit button enabled (client validation fires on submit).
- Show/hide toggle defaults to "Show" / 👁 icon, `aria-pressed=false`.

### Loading (submit in flight)
- Submit button → label "Signing in…", `aria-busy=true`, `disabled`.
- Email/password inputs `readonly` (not disabled, so values stay in a11y tree).
- Spinner inline-leading inside button. Form-level `aria-busy=true` on `<form>`.

### Error
- 401: `#form-error` shows "Email or password is incorrect." `role=alert` announces; focus moves to error region (`tabindex=-1`). Inputs get `aria-invalid=true`. Password field cleared.
- 400 validation: per-field error text under each input, `<input aria-describedby="email-err">`. First invalid field receives focus.
- Network/500: "We couldn't reach the server. Try again." with a Retry button inside the error region.

### Success
- Brief "Signed in. Redirecting…" status, then route to `/` (or `returnTo` param).

---

## Focus order (Tab)
1. Skip-to-content link (visually hidden until focused)
2. Logo (link to /)
3. Help / Lang in header
4. Email input
5. Password input
6. Show/hide toggle button
7. Forgot password link
8. Sign in button
9. Create account link

Submit is reachable via Enter from any input. Esc clears the error region.

## Accessibility checklist
- All inputs have `<label for>` (no placeholder-only labels).
- Error region: `role=alert aria-live=assertive aria-atomic=true`.
- Show/hide button: `<button type=button aria-pressed aria-label="Show password" / "Hide password">`. Does not submit form.
- Color contrast: body text #1F2328 on #FFFFFF = 15.9:1; primary button #0969DA on #FFFFFF = 4.55:1 ✅; error text #CF222E on #FFFFFF = 5.87:1 ✅.
- Focus ring: 2px solid #0969DA, 2px offset, never removed.
- Touch target ≥ 44×44 px (mobile button is 48px).
- Inputs support browser autofill (`autocomplete=username` / `current-password`).
- No CAPTCHA on first attempt (defer to security-hardening-squad rate-limit response).
