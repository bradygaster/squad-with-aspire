# Remember Me — Login Form (Wireframe & Contract)

**Scope:** "Remember me" checkbox on the login screen at `apps/web` (travel-assistant).
**Status:** Design contract locked. Binds app-dev (DOM/wiring), QT (selectors/events), security (token lifetime).
**Owners:** experience-design-squad (this doc) · application-development-squad (implementation) · security-hardening-squad (token policy) · quality-testing-squad (verification).

---

## 1. Decisions (locked)

| ID | Decision | Rationale |
|----|----------|-----------|
| D1 | **Label copy: "Keep me signed in"** | Plain-language; describes the *outcome* (session persistence) rather than the *mechanism* ("remember"). Matches GitHub/Google/Stripe. Avoids ambiguity with browser autofill. |
| D2 | **Placement: directly below password field, above the submit button**, left-aligned with the field column | Standard mental model; user evaluates the choice in the same visual block as credentials and before commit. |
| D3 | **Default: unchecked** | Privacy-respecting default. Long-lived tokens are opt-in. Aligns with shared-device safe path. |
| D4 | **Native `<input type="checkbox">` + `<label>`** — no custom widget, no ARIA shims | Built-in keyboard, focus ring, SR announcement, forms-API integration. Custom widgets here are pure regression risk. |
| D5 | **Microcopy below label:** *"Use only on a device you trust."* — rendered as `<small>` linked via `aria-describedby` | Concrete actionable guidance; avoids fear-language ("danger", "risky"). Always visible (not a tooltip) — hover-only disclosures fail mobile and SR. |
| D6 | **State submitted to backend as boolean field `rememberMe` in the login POST body** | One source of truth; no client-side token-lifetime negotiation. |
| D7 | **No persistence of the checkbox state itself across sessions.** Always renders unchecked on a fresh page load. | The token *is* the persistence mechanism. Re-checking the box on logout-then-login is intentional friction. |
| D8 | **Disabled (not hidden) while submit is in flight**, alongside email/password | Prevents mid-flight toggle that would desync from the request payload. Use existing `<AuthForm>` `aria-busy` pattern. |

---

## 2. Wireframes

### 2.1 Desktop (1280px) — default state

```
┌─────────────────────────────────────────────────────────┐
│  Travel Assistant                          [Light·System·Dark] │
├─────────────────────────────────────────────────────────┤
│                                                         │
│                    Sign in                              │
│                                                         │
│       Email                                             │
│       ┌─────────────────────────────────────┐           │
│       │ you@example.com                     │           │
│       └─────────────────────────────────────┘           │
│                                                         │
│       Password                                          │
│       ┌─────────────────────────────────────┐           │
│       │ ••••••••••                     [👁]  │           │
│       └─────────────────────────────────────┘           │
│                                                         │
│       ☐ Keep me signed in                               │
│         Use only on a device you trust.                 │
│                                                         │
│       ┌─────────────────────────────────────┐           │
│       │           Sign in                    │           │
│       └─────────────────────────────────────┘           │
│                                                         │
│       Forgot password?      Create account              │
└─────────────────────────────────────────────────────────┘
```

### 2.2 Mobile (375px) — default state

```
┌───────────────────────────┐
│ Travel Assistant      ☰   │
├───────────────────────────┤
│                           │
│       Sign in             │
│                           │
│  Email                    │
│  ┌─────────────────────┐  │
│  │ you@example.com     │  │
│  └─────────────────────┘  │
│                           │
│  Password                 │
│  ┌─────────────────────┐  │
│  │ ••••••••       [👁]  │  │
│  └─────────────────────┘  │
│                           │
│  ☐ Keep me signed in      │
│    Use only on a device   │
│    you trust.             │
│                           │
│  ┌─────────────────────┐  │
│  │      Sign in        │  │
│  └─────────────────────┘  │
│                           │
│  Forgot password?         │
│  Create account           │
└───────────────────────────┘
```

### 2.3 Focus state

Focus ring on the **checkbox box** (not the row, not the label text). Uses existing `--color-focus-ring` token (DM-001). 2px outline, 2px offset. Label text underlines on focus for additional non-color affordance.

```
  ┌─┐
  │☐│◀── 2px solid var(--color-focus-ring), offset 2px
  └─┘  Keep me signed in
       ──────────────── (underline added)
       Use only on a device you trust.
```

### 2.4 Hover state (pointer devices only)

Background of the **full clickable row** (checkbox + label) gets `--color-bg-elevated`. Cursor: pointer over both box and label. No hover effect on touch devices (`@media (hover: hover)`).

### 2.5 Checked state

```
  ☑ Keep me signed in
    Use only on a device you trust.
```

Check mark uses `--color-text-on-brand` on `--color-brand` fill — same primary-action treatment as the submit button so the affordance is visually coherent.

### 2.6 Error state

**The checkbox itself never enters an error state.** Login form-level errors (bad credentials, 503, 429) render in the existing `<AuthFormBanner>` above the email field per the auth-error contract. The checkbox row stays neutral. Its value is preserved across a failed submit so the user does not re-toggle it.

### 2.7 Disabled (in-flight) state

While `aria-busy="true"` on the form (≥300ms per auth-503 contract):
- Checkbox: `disabled` attribute, opacity 0.6, cursor: not-allowed
- Label text and microcopy: opacity 0.6
- No interaction; value frozen at the moment of submit

---

## 3. DOM contract (binding for app-dev, QT, security)

```html
<div class="form-row form-row--checkbox" data-field="remember-me">
  <input
    type="checkbox"
    id="login-remember-me"
    name="rememberMe"
    data-testid="login-remember-me"
    aria-describedby="login-remember-me-hint"
  />
  <label for="login-remember-me">Keep me signed in</label>
  <small id="login-remember-me-hint" class="form-hint">
    Use only on a device you trust.
  </small>
</div>
```

**Locked attribute contract** (do not rename — bound by tests and analytics):

| Attribute | Value | Owner |
|-----------|-------|-------|
| `id` | `login-remember-me` | XD |
| `name` | `rememberMe` | XD + security (POST body field) |
| `data-testid` | `login-remember-me` | QT (E2E selector) |
| `aria-describedby` | `login-remember-me-hint` | XD (a11y) |
| Hint `id` | `login-remember-me-hint` | XD (a11y) |

**CSS class contract** is non-load-bearing — app-dev may rename `.form-row--checkbox` freely; tests MUST NOT depend on classes.

---

## 4. Accessibility contract

| Concern | Requirement |
|---------|-------------|
| Widget | Native `<input type="checkbox">`. No `role="checkbox"`, no `aria-checked`, no `tabindex` overrides. |
| Label association | `<label for>` ↔ `<input id>`. Clicking label toggles the box. |
| Description | `aria-describedby` points at the hint `<small>`. SR announces label + state + hint on focus. Verified in NVDA 2025.1 + JAWS 2024 + VoiceOver macOS 15. |
| Keyboard | `Space` toggles. `Tab` moves to/from. No custom key handlers. |
| Focus indicator | Visible 2px ring on box; passes 3:1 contrast against both `--color-bg` and `--color-bg-elevated` (verified in DM-001 matrix). Underline appears on label as non-color secondary affordance. |
| Touch target | Full row (box + label + hint) clickable area ≥44×44px on mobile (375px), ≥32×32px on desktop. Match DM-001 toggle contract. |
| Reduced motion | No animation on check/uncheck transition. Static state change. |
| Color independence | Checked state distinguishable without color (check-mark glyph + filled box). |
| Screen reader announcement | "Keep me signed in, checkbox, not checked. Use only on a device you trust." (NVDA verbatim). State changes announced on toggle. |
| Error context | Form-level errors are NOT bound to the checkbox via `aria-errormessage` — errors apply to credentials, not to the remember-me choice. |

---

## 5. Analytics event contract

Single event, fired on **form submit** (not on toggle — toggle-without-submit is not interesting and would bias the rate):

```json
{
  "event": "auth.login.submitted",
  "properties": {
    "rememberMe": true | false,
    "...existing login submit fields"
  }
}
```

No separate `auth.remember_me.toggled` event. Rate of opt-in derived as `count(rememberMe=true) / count(auth.login.submitted)` over the period. Rationale: keeps the schema additive (one new field on an existing event) and gives security the signal they need (what fraction of sessions are long-lived) without adding a high-cardinality interaction stream.

**Telemetry field name `rememberMe` (camelCase)** matches the POST body field. Single name across the wire.

---

## 6. Open items for other squads

### → application-development-squad
1. **Form state preservation across failed submit:** when login returns 4xx/5xx, the `rememberMe` value MUST persist in the React form state (do not reset). Email also persists per existing contract; password clears per existing contract; remember-me MUST follow email, not password.
2. **`<AuthForm>` `aria-busy` extension:** confirm the existing in-flight disable pattern covers the new checkbox automatically via `fieldset[disabled]` or equivalent. If the form uses per-field `disabled`, add the checkbox to that list.
3. **POST body field name** is `rememberMe` (boolean). Confirm the existing login endpoint already accepts unknown fields gracefully, or coordinate with security on the wire schema bump.

### → security-hardening-squad
1. **Token-lifetime policy** when `rememberMe=true`: please publish the exact lifetime (e.g., 30d sliding vs 90d absolute) and whether it's a separate cookie/token type or a flag on the existing one. UX needs this only to decide whether the disclosure microcopy needs a number ("…signs you in for up to 30 days") — current spec uses qualitative copy ("Use only on a device you trust") which works for any lifetime ≤90d. If the policy is >90d we should reconsider the copy.
2. **Default-unchecked is a security-positive default** (D3). Flag if you need it explicitly logged in the threat model.
3. **No client-side storage of the checkbox state itself** (D7). The persistence mechanism is the token, not localStorage. Please confirm this matches your model.

### → quality-testing-squad
1. **E2E selector locked:** `[data-testid="login-remember-me"]`.
2. **Smoke matrix to add to the login suite:**
   - R1: Renders unchecked by default on every page load (including after logout)
   - R2: `Space` key toggles when focused
   - R3: Clicking the label toggles
   - R4: Value persists in form state across a failed submit (bad credentials)
   - R5: Disabled during submit (`aria-busy=true` window)
   - R6: POST body contains `rememberMe: true` when checked, `rememberMe: false` when unchecked
   - R7: Analytics `auth.login.submitted` event payload contains `rememberMe` boolean
   - R8: SR announces label + state + hint on focus (axe + manual NVDA spot)
   - R9: Focus ring visible in both light and dark theme (DM-001 contrast contract)
   - R10: Touch target ≥44px on 375px viewport

### → review-deployment-squad
EMU blocks `git push` from this session. Branch `xd/remember-me-design` exists locally at `C:\Users\tamirdresher\source\repos\travel-assistant`. Please transplant to `tamirdresher/travel-assistant` and open the design PR. No code changes — docs only.

---

## 7. Non-goals (out of scope for this design)

- Browser autofill behavior (handled by `autocomplete` attributes on email/password, not on the checkbox)
- "Trust this device" as a separate concept from session length (single-knob design here; if security wants a separate device-trust flow, that is a follow-up wireframe)
- Biometric / passkey unlock as an alternative to remember-me (separate feature)
- Admin policy to disable the checkbox for enterprise tenants (no enterprise tier yet)
- Logout-everywhere / session management UI (separate screen)
