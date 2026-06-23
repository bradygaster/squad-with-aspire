# Auth wireframes

Lo-fi wireframes for the authentication flow. Owner: **experience-design-squad**.

| Screen   | File                 | Route       | API endpoint              |
|----------|----------------------|-------------|---------------------------|
| Login    | [login.md](./login.md)       | `/login`    | `POST /api/auth/login`    |
| Register | [register.md](./register.md) | `/register` | `POST /api/auth/register` |

## Shared design tokens (lo-fi)
- Card: max-width 420px on desktop, full-bleed with 16px gutter on mobile.
- Primary button: bg `#0969DA`, fg `#FFFFFF`, 4.55:1 contrast.
- Error text/icon: `#CF222E` on `#FFFFFF`, 5.87:1.
- Body text: `#1F2328` on `#FFFFFF`, 15.9:1.
- Focus ring: 2px solid `#0969DA`, 2px offset, never removed.
- Min touch target: 44×44 px (mobile uses 48px tall inputs/buttons).
- Breakpoints: mobile 375px, desktop 1280px. Layout reflows fluidly between.

## Cross-cutting accessibility rules
- All inputs use `<label for>` — no placeholder-as-label.
- Error region per form: `role=alert aria-live=assertive aria-atomic=true`, hidden until populated, receives focus on error.
- Per-field errors: `aria-invalid=true` + `aria-describedby` pointing at the message id.
- Show/hide password toggle: `<button type=button aria-pressed>`; does not submit. Label flips "Show password" / "Hide password".
- Loading state: form `aria-busy=true`, submit shows spinner + label change, inputs go `readonly` (not `disabled`, so values stay readable to AT).
- Skip-to-content link is the first focusable element on every page.
- Forms support browser autofill via correct `autocomplete` tokens (`username`, `current-password`, `new-password`).

## Handoffs
- **application-development-squad**: please confirm API response shapes match assumptions in each file's header (see "Open questions" at bottom of `register.md`).
- **security-hardening-squad**: 429 response shape drives the rate-limit countdown UI in register.md.
- **quality-testing-squad**: the State sections in each file double as the test matrix (empty / loading / error variants × {desktop, mobile}). a11y assertions: label association, focus on error, contrast, keyboard tab order listed above.
