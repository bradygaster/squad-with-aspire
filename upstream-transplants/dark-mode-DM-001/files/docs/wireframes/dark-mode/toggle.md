# Dark-Mode Theme Toggle — Wireframes (DM-001)

Status: **Locked** for v0.5.0. Owner: experience-design-squad.
Consumer: application-development-squad (DM-002 ThemeProvider + toggle UI).

Companion spec: `docs/design/dark-mode-tokens.md` (palette, storage,
no-FOUC contract). This document covers component shape, states,
placement, and accessibility behavior only.

## 1. Component choice

**Segmented radiogroup**, three options: Light · Dark · System.
Rationale and rejected alternatives are ratified in tokens §7 D1.

```
┌─────────────────────────────────────┐
│  ☀  Light  │  ◐  System  │  ☾  Dark │   <- selected option has filled bg
└─────────────────────────────────────┘
```

- Icons are decorative (`aria-hidden="true"`). Labels are the
  authoritative text. Mobile collapses to icon-only with the label
  still present as accessible text (see §5).
- Order is `Light · System · Dark` — System is centered because it's
  the recommended default and reads as a "middle ground".

## 2. Placement

App header, right cluster, **immediately left of the user avatar**:

```
┌──────────────────────────────────────────────────────────────────────┐
│ ✈ Travel Assistant     Search …          [☀│◐│☾]   🔔   ( T )       │
└──────────────────────────────────────────────────────────────────────┘
                                            └─toggle    └avatar
```

Rationale: tokens §7 D2. Toggle is in the persistent header so every page
has identical access; settings-menu placement is rejected.

## 3. Anatomy + tokens

| Part            | Token (light → dark)                                  |
| --------------- | ----------------------------------------------------- |
| Container bg    | `--color-bg-elevated`                                 |
| Container border| `--color-border-default`, 1px, radius 8px             |
| Option text     | `--color-text-secondary`                              |
| Selected text   | `--color-text-primary`                                |
| Selected bg     | `--color-bg` (popped, brighter than container)        |
| Selected border | `--color-border-default` inset 1px                    |
| Hover text      | `--color-text-primary`                                |
| Hover bg        | `--color-bg` at 50% opacity                           |
| Focus ring      | 2px solid `--color-focus-ring`, 2px offset            |
| Pressed bg      | `--color-bg-elevated` darkened 4% (or `color-mix`)    |

Height: **32px** desktop · **44px** mobile (touch-target min).
Option width: `auto` with min `64px` desktop, `44px` mobile (icon-only).
Gap between options: 0 (shared dividers via 1px border-left on 2nd/3rd).

## 4. State renderings (desktop 1280px)

Each row is the **entire control**; arrow marks which option is in that
visual state.

### 4.1 Default — System selected (first paint default for new users)

```
┌────────────┬─────────────┬────────────┐
│ ☀  Light   │ ◐  System ▣ │ ☾  Dark    │   ▣ = filled / selected
└────────────┴─────────────┴────────────┘
```

### 4.2 Hover on "Dark" — System still selected

```
┌────────────┬─────────────┬────────────┐
│ ☀  Light   │ ◐  System ▣ │ ☾  Dark  ░ │   ░ = hover bg
└────────────┴─────────────┴────────────┘
```

### 4.3 Keyboard focus on "Light" — System still selected

```
╔════════════╗─────────────┬────────────┐
║ ☀  Light   ║ ◐  System ▣ │ ☾  Dark    │   ║═ = 2px focus ring, 2px offset
╚════════════╝─────────────┴────────────┘
```

The focus ring wraps the **focused option**, not the container. With
roving tabindex (§6), only one option is tabbable; arrow keys move focus
*and* selection.

### 4.4 Pressed on "Dark" (mid-activation, before selection commits)

```
┌────────────┬─────────────┬────────────┐
│ ☀  Light   │ ◐  System   │ ☾  Dark  ▼ │   ▼ = pressed (slightly darker)
└────────────┴─────────────┴────────────┘
```

### 4.5 After committing "Dark"

```
┌────────────┬─────────────┬────────────┐
│ ☀  Light   │ ◐  System   │ ☾  Dark  ▣ │
└────────────┴─────────────┴────────────┘
```

Theme application is **synchronous** (no transition, see tokens §6).
The whole app repaints in dark colors in the same frame as the selection
visual updates.

## 5. Mobile (375px)

Icon-only collapse; label remains in `aria-label` of each radio:

```
┌─────┬─────┬─────┐
│  ☀  │  ◐  │  ☾  │     each option 44×44px (touch target)
└─────┴─────┴─────┘
```

Placement on mobile: the toggle moves into the overflow menu on screens
< 480px to keep the header from wrapping. Trigger: existing `≡` menu
button. Inside the menu, the segmented control renders at full label
width (icon + text), one per row is **not** acceptable — keep horizontal
segmented form so all three states stay co-visible.

## 6. Accessibility contract

Authoritative; QA gates green against this list.

### 6.1 Roles + names

```html
<div role="radiogroup" aria-label="Theme">
  <button role="radio" aria-checked="false" tabindex="-1" aria-label="Light theme">
    <svg aria-hidden="true">…</svg><span>Light</span>
  </button>
  <button role="radio" aria-checked="true"  tabindex="0"  aria-label="System theme (follow OS)">
    <svg aria-hidden="true">…</svg><span>System</span>
  </button>
  <button role="radio" aria-checked="false" tabindex="-1" aria-label="Dark theme">
    <svg aria-hidden="true">…</svg><span>Dark</span>
  </button>
</div>
```

- `radiogroup` accessible name: **"Theme"** (not "Theme toggle" — toggle
  is implementation).
- Each `radio` has an `aria-label` that includes the word "theme" so it
  is unambiguous when read out of group context (Voiceover rotor, etc.).

### 6.2 Keyboard

| Key                | Behavior                                                  |
| ------------------ | --------------------------------------------------------- |
| `Tab`              | Move into / out of the radiogroup as a single stop        |
| `Right` / `Down`   | Move focus + selection to next option, wraps              |
| `Left`  / `Up`     | Move focus + selection to previous option, wraps          |
| `Home`             | Focus + select first option (Light)                       |
| `End`              | Focus + select last option (Dark)                         |
| `Space` / `Enter`  | No-op on already-selected option; selecting via arrows is the canonical interaction (WAI-ARIA radiogroup pattern). Mouse/touch click still works. |

Selecting via arrow IS the activation — this is the standard ARIA radio
pattern. No need for a separate "confirm" press.

### 6.3 Screen-reader announcement on change

When selection changes, the rendered theme is announced through a
visually-hidden `aria-live="polite"` region scoped to the toggle:

```html
<span class="sr-only" aria-live="polite" aria-atomic="true">
  Theme changed to dark
</span>
```

Copy variants (the only three allowed):

- `Theme changed to light`
- `Theme changed to dark`
- `Theme changed to system (currently {light|dark})` — the parenthetical
  reflects the **resolved** value at announcement time so SR users hear
  what they'll actually see.

Throttle: emit at most once per 500ms to avoid burst announcements when
the user arrow-keys quickly across options. Final-state-wins.

### 6.4 Focus visibility

`--color-focus-ring` is the same token in both themes (brand). 2px solid
ring, 2px offset. Must remain visible against the *option's* background
in every state above. Verified: brand `#0969da` vs `#ffffff` = 5.48:1;
brand-dark `#58a6ff` vs `#0d1117` = 8.25:1. Both ≥ 3:1 — pass.

### 6.5 Forced-colors mode

In `@media (forced-colors: active)`:
- Container border → `CanvasText`
- Selected option bg → `Highlight`, text → `HighlightText`
- Unselected text → `CanvasText`
- Focus ring → `Highlight` (UA default is fine)
- Do **not** apply `forced-color-adjust: none`.

### 6.6 Reduced motion

No animation on theme apply, ever (tokens §6). The selection-pill movement
within the segmented control may use a 80ms transform transition; it
collapses to 0ms under `prefers-reduced-motion: reduce`.

## 7. Initial-render & no-FOUC behavior

The toggle's *visual* selected state is driven by what's in
`localStorage` (or `"system"` when absent), **not** by `data-theme`.
This is the only place those two diverge:

| storage  | data-theme (resolved) | toggle shows selected |
| -------- | --------------------- | --------------------- |
| `light`  | `light`               | Light                 |
| `dark`   | `dark`                | Dark                  |
| `system` | `light` or `dark`     | **System**            |
| absent   | `light` or `dark`     | **System**            |

App-dev: hydrate the toggle's initial `aria-checked` from a synchronous
read of `localStorage["theme"]` inside the same component as the toggle.
No flash-on-mount: the no-FOUC script already painted the correct
*colors*; the toggle's *highlight* renders correctly on first React paint
because the read is synchronous in the component body.

## 8. Open items handed back

None. D1, D2, D3 are ratified in tokens §7.

## 9. Handoffs

- **DM-002 (app-dev):** Build `<ThemeProvider>` + `<ThemeToggle>` per §3–§7.
  Mount toggle in header right cluster per §2.
- **DM-003 (app-dev):** Implement the no-FOUC script per tokens §5 — byte-stable.
- **DM-004 (QA):** Tests in `apps/web` cover:
  - All keyboard interactions in §6.2
  - SR announcement copy in §6.3 (three exact strings)
  - Contrast assertions per tokens §3 tables
  - First-paint has correct `data-theme` (no FOUC) under jsdom + Playwright
- **DM-005 (sec-hard):** Compute CSP `sha256-…` hash of tokens §5 script bytes.
- **DM-006 (az-infra):** Telemetry events `theme.toggle.changed` carrying
  `{ from, to, resolvedTheme }` (no PII). Optional, deferrable.
