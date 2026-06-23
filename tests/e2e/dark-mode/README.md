# DM-004 — Dark-mode test suite (red-until-green)

Tracks ideation-research-planning-squad DM-004. Contract tests authored
against XD's `docs/design/dark-mode-tokens.md` + wireframes; bind to DM-002
implementation when it lands.

## Layout

| Path | Spec § | State |
| --- | --- | --- |
| `tests/unit/theme/contrast.test.ts` | §1 WCAG AA | spec-presence gated |
| `tests/unit/theme/toggle-a11y.test.ts` | §2 a11y + keyboard | component-presence gated |
| `tests/unit/theme/storage-fallback.test.ts` | §3 corrupted/disabled storage | always-on (pure) |
| `tests/unit/theme/state-machine.test.ts` | §5 light↔dark↔system | always-on (pure) |
| `tests/e2e/dark-mode/persistence.spec.ts` | §3 + §4 (FOUC) | Playwright, env-gated on `DM_E2E_BASE_URL` |

## Binding contract for DM-002

- HTML must expose `data-theme="light"|"dark"` on `<html>`.
- Storage key: `ta.theme`, values `light|dark|system`, anything else → system.
- Toggle: `role=radiogroup` w/ accessible name `Theme`, three radios `light|dark|system`.
- Pre-paint inline script in `<head>` reads storage + matchMedia and writes
  `data-theme` *synchronously* before first frame (asserted in §4 FOUC test).
- Provider must never write anything but `light` or `dark` as the resolved
  attribute, regardless of pref / OS state (§5 invariant).

## Coverage gate
≥90% statement coverage on the ThemeProvider module once DM-002 lands.
Cross-browser: chromium, firefox, webkit.

## Out of scope (deferred)
- axe-core run wiring (needs DOM harness — Playwright `@axe-core/playwright`
  will be added with DM-002 component scaffold).
- Visual-regression screenshots for both themes.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
