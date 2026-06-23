# ADR-0002: Blazor Component Library

- **Status:** Accepted
- **Date:** 2026-06-23
- **Owner:** experience-design-squad
- **Related:** ADR-0001 (Blazor Server+WASM hybrid), XD-3 (a11y baseline), XD-2 (split-canvas wireframes), XD-5 (voice & tone)
- **Supersedes:** XD-5 "shadcn-equivalent" placeholder from v0.1 backlog

## Context

ADR-0001 picked Blazor Server+WASM hybrid over Next.js. The original XD design baseline assumed a React stack with shadcn/ui + Tailwind. We need a Blazor component library that:

1. **Meets WCAG 2.2 AA out of the box** (XD-3 — merge-blocking, non-negotiable)
2. **Has the breadth shadcn gives a React team** (form controls, dialog, popover, command palette, toast, skeleton, table)
3. **Plays well with SignalR streaming** (assistant tokens land via `aria-live="polite"`; itinerary patches mutate without focus loss)
4. **Supports the split-canvas layout** in `docs/design/wireframes/split-canvas.md` (chat 40% / itinerary 60% desktop; itinerary-primary + chat-peek mobile)
5. **Allows token customization** so `docs/design/tokens.json` (already AA/AAA-contrast verified) can drive theme without forking the lib
6. **Is maintained** (last release within 90 days; >1 maintainer)

## Options evaluated

| Lib | WCAG 2.2 AA | Component breadth | Token theming | Streaming-friendly | Maintenance | License |
|---|---|---|---|---|---|---|
| **MudBlazor** | ✅ Strong; documented a11y per component; ARIA roles correct on Dialog/Menu/Autocomplete | ✅ Wide (90+ components incl. Autocomplete, Drawer, Snackbar, Skeleton, Virtualize) | ✅ `MudTheme` class accepts arbitrary tokens; CSS-var bridge straightforward | ✅ Components don't steal focus on prop updates; `MudList`/`MudVirtualize` handle append-only streams | ✅ Active, monthly releases, MIT | MIT |
| **FluentUI Blazor** | ✅ Microsoft-owned; matches Azure portal a11y posture | 🟡 Good but narrower; some patterns (command palette, popover trigger) require composition | 🟡 Design-tokens-based but opinionated toward Fluent 2 aesthetic; deviating costs effort | ✅ Fine | ✅ Active, MS-backed, MIT | MIT |
| **Radzen Blazor** | 🔴 Weakest of the three; multiple open a11y issues on Dialog and DataGrid; no per-component a11y docs | ✅ Very wide | 🟡 Theme builder, limited token override | ✅ Fine | ✅ Active, free tier MIT | MIT (free) / commercial (Pro) |
| **Hand-rolled** | ⚠️ Depends entirely on us — high risk of regression without dedicated a11y review per component | 🔴 We build everything | ✅ Full control | ✅ Full control | 🔴 We maintain it | n/a |

## Decision

**Adopt MudBlazor as the primary Blazor component library.**

Rationale:

1. **A11y posture is the dealbreaker.** XD-3 made WCAG 2.2 AA merge-blocking. MudBlazor documents ARIA roles per component, has fewest open a11y issues, and Pris's per-PR checklist (`.github/pull_request_template.md` Accessibility block) is enforceable against it without per-component remediation. Radzen fails this gate. FluentUI passes but FluentUI's narrower breadth means we'd hand-roll a popover/command-palette anyway — and those are exactly the components where a11y regressions happen.
2. **Token alignment is mechanical.** `docs/design/tokens.json` (design-tokens.org schema) maps to `MudTheme` via a small bridge component. No fork needed. FluentUI would resist re-tokenization toward our voice (XD-5 — "warm, direct, grounded" — not Fluent 2's neutral system feel).
3. **Aesthetic-neutral.** MudBlazor's Material-ish defaults are easy to flatten/restyle to match our split-canvas wireframes without fighting the lib. FluentUI's Azure-portal look would constrain XD-2's chat-canvas design choices.
4. **Streaming compatibility verified.** `MudList` with keyed children + `MudVirtualize` handle the streaming-token + itinerary-patch case without focus loss or AT re-announcement storms — validated against `docs/design/conversation-ux.md` 7-event SignalR vocabulary.

## Rejected alternatives

- **FluentUI Blazor:** A11y is fine; breadth is the issue. We'd compose a popover, command palette, and skeleton ourselves — each a per-component a11y risk that defeats the point of picking a "passes the gate" library.
- **Radzen Blazor:** A11y gaps disqualify it under XD-3. Free tier license is also ambiguous on extension/forking; Pro tier adds cost with no a11y improvement.
- **Hand-rolled:** Wrong stage. Maybe revisit at v1.0 if MudBlazor blocks a specific interaction.

## Consequences

### Positive
- Single canonical lib; one a11y baseline to defend per PR.
- Token bridge means `tokens.json` stays authoritative — no token drift between design and code.
- MIT license, no commercial entanglement.

### Negative / accepted risks
- **Material-ish default aesthetic.** Must explicitly flatten / restyle to match XD-2 wireframes. Mitigation: theme overrides land in `app.css` + `MudTheme` config, not per-component.
- **MudBlazor's Dialog focus-trap can conflict with our `pending` patch coercion pattern** (XD-2 §6) if a dialog opens mid-stream. Mitigation: any modal triggered during `streaming` or `pending-patch` states must defer until `turn.end`. Documented in `docs/design/conversation-ux.md`.
- **No first-party command palette.** We compose `MudAutocomplete` + `MudDialog`. Acceptable — same effort as in FluentUI.

### Follow-on work for experience-design-squad
- **XD-6a — Convert `docs/design/components.md` v0.1 inventory from shadcn refs to MudBlazor refs.** Same screen × state matrix from `docs/design/ia.md` §2; just swap component names + Razor syntax samples. (Owner: XD. Blocker for: APP-2 chat UI work.)
- **XD-6b — Ship MudTheme ↔ tokens.json bridge.** Single Razor partial `_AppTheme.razor` that reads tokens.json at build time and emits `MudTheme`. (Owner: XD pairs with app-dev. Blocker for: any UI PR.)
- **XD-6c — A11y per-state axe iteration on MudBlazor components.** Replaces QA-3b (state-fixture URLs) follow-up. (Owner: XD + QA jointly. Blocker for: full XD-4 sign-off.)

### Follow-on work for other squads
- **application-development-squad:** Add `MudBlazor` NuGet to `src/TravelAssistant.Web` (or wherever the Blazor host lives post-ADR-0001). Register `AddMudServices()` in `Program.cs`. Wrap root in `<MudThemeProvider>`/`<MudDialogProvider>`/`<MudSnackbarProvider>`. Pin to the latest 7.x or current stable.
- **quality-testing-squad:** No change to the axe harness — selectors are still data-testid-driven. QA-3b (per-state fixtures) now lands against MudBlazor components rather than shadcn; same contract.
- **review-deployment-squad:** Add MudBlazor to `dotnet list package --vulnerable` allowlist review (it's clean today, just acknowledge it's a new top-level dep).
- **security-hardening-squad:** No action. MudBlazor doesn't introduce auth/secrets surface area.

## Validation

- ✅ WCAG 2.2 AA gate: passes (per MudBlazor a11y docs + current open-issue scan, 2026-06)
- ✅ Token bridge: prototyped against `tokens.json` schema, no schema changes needed
- ✅ Streaming compat: `MudList` + keyed children verified against 7-event SignalR vocab in `docs/design/conversation-ux.md`
- ✅ Voice (XD-5): default Material micro-copy can be overridden per-component; no lib-imposed strings will reach the user

## References
- ADR-0001 — Web framework (Blazor Server+WASM hybrid)
- `docs/design/ia.md` — Information architecture & 7×6 screen-state matrix
- `docs/design/a11y-checklist.md` — WCAG 2.2 AA per-PR gate
- `docs/design/tokens.json` — Authoritative design tokens
- `docs/design/conversation-ux.md` — SignalR event vocabulary
- MudBlazor: https://mudblazor.com
- Pris's reject criteria — `.github/pull_request_template.md` Accessibility block
