# Dark-Mode UX Smoke Checklist — PR #28 + DM-006 Sibling

**Owner:** experience-design-squad
**Companion to:** `MERGE-RUNBOOK.md` §5 (S1–S11 technical smoke)
**Scope:** User-visible behaviour. Technical/CSP byte-checks are covered by S1–S11; this file covers what a human (or SR user) actually perceives in the browser.
**Run when:** After both PR #28 and the DM-006 sibling PR are merged to `main`, before the `v0.5.0` tag is cut.
**Pass bar:** Every U-row must pass on **both** Chromium-latest **and** Firefox-latest, **both** themes, **both** desktop (≥1024px) and mobile (≤480px). One spot-check on Safari is sufficient.

---

## Pre-flight

Browser DevTools open. Application → Local Storage → clear `ta.theme` between U1–U3. Use `prefers-color-scheme` media-emulation in DevTools for U2 / U4 / U5.

---

## U1 — First paint, no stored preference, OS=light

| # | Step | Expect |
|---|---|---|
| U1.1 | DevTools → emulate `prefers-color-scheme: light`. Clear `ta.theme`. Hard-reload `/`. | No flash of dark content. First paint is light. |
| U1.2 | Inspect `<html>` attributes. | `data-theme="light"` present **before** React hydrates (set by no-FOUC IIFE). `data-theme` is never `"system"`. |
| U1.3 | Inspect `localStorage["ta.theme"]`. | **Unset.** First visit must not write a value. |
| U1.4 | Header → Theme control. | `role="radiogroup"`, `aria-label="Theme"`. Three radios: Light · System · Dark, with **System** centered and pre-selected (`aria-checked="true"`). |

## U2 — First paint, no stored preference, OS=dark

| # | Step | Expect |
|---|---|---|
| U2.1 | DevTools → emulate `prefers-color-scheme: dark`. Clear `ta.theme`. Hard-reload `/`. | No flash of light content. First paint is dark. |
| U2.2 | `<html data-theme>` value. | `"dark"`. |
| U2.3 | `localStorage["ta.theme"]`. | **Unset.** |
| U2.4 | Theme control selected radio. | **System** still pre-selected (storage is `"system"` implicitly). Light/Dark are not selected. |

## U3 — Explicit pick persists across reload

| # | Step | Expect |
|---|---|---|
| U3.1 | Click **Dark** radio. | `<html data-theme="dark">` immediately. `localStorage["ta.theme"] === "dark"`. |
| U3.2 | Hard-reload. | No flash. First paint dark. **Dark** radio still selected. |
| U3.3 | Click **Light**. Reload. | First paint light. **Light** radio selected. `ta.theme === "light"`. |
| U3.4 | Click **System** with OS=dark. | `<html data-theme="dark">`. `ta.theme === "system"`. |

## U4 — OS change while on `system`

| # | Step | Expect |
|---|---|---|
| U4.1 | With `ta.theme === "system"` and OS=light, page open. Toggle DevTools emulation to dark. | `<html data-theme>` flips to `"dark"` live, **without page reload**. No animation of background/foreground (banned per DM-001 vestibular contract). |
| U4.2 | `ta.theme` after flip. | Still `"system"`. Never overwritten to `"dark"`. |
| U4.3 | With `ta.theme === "light"` and OS toggling. | Theme stays **light**. OS signal is ignored when explicit choice is set. |

## U5 — Token coverage (visual sweep)

For each theme, scan a fully populated page (any route with cards, form, link, status banner, focused input):

| Surface | Both themes must satisfy |
|---|---|
| Body bg / text | Uses `--color-bg` + `--color-text-primary`. Body copy contrast ≥ 4.5:1 (already verified in `dark-mode-tokens.md` §"Contrast verification matrix"). |
| Card / elevated panel | `--color-bg-elevated` distinguishable from `--color-bg` without relying on a load-bearing border alone. |
| Secondary text | `--color-text-secondary` legible against both bg and bg-elevated. |
| Brand link | Visible, contrast ≥ 4.5:1, hover state differs perceptibly. |
| Status banners | info / success / warn / danger all distinguishable from each other **without** depending on hue alone (icon or label carries semantics). |
| Borders | `--color-border-subtle` is decorative only — no UI affordance is conveyed by it alone. `--color-border-default` is visible. |
| Focus ring | `--color-focus-ring` visible on **both** themes against **both** bg and bg-elevated. ≥ 3:1 against adjacent surface. |

## U6 — No-FOUC inline script integrity

| # | Step | Expect |
|---|---|---|
| U6.1 | View source. Locate the inline `<script>` in `<head>` (before any stylesheet that emits color). | Script body byte-identical to `docs/wireframes/dark-mode/no-fouc-contract.md`. (S1 verifies this hash mechanically; U6 confirms it is reachable in served HTML.) |
| U6.2 | DevTools → Network → throttle to "Slow 3G". Hard-reload `/` with `ta.theme === "dark"`. | Background is dark from the very first frame. No white flash. |
| U6.3 | Same as U6.2 but with `ta.theme === "light"` on OS=dark. | Background is light from the very first frame. |

## U7 — DM-006 CSP enforcement (UX-observable side)

Technical byte-verify lives in S11. This row catches *user-visible breakage* if the hash drifts.

| # | Step | Expect |
|---|---|---|
| U7.1 | Hard-reload `/`. Open DevTools → Console. | No CSP violation reports referencing the inline IIFE. (In Report-Only mode: report endpoint receives nothing for IIFE.) |
| U7.2 | If a CSP `script-src` violation fires for the inline IIFE | **FAIL.** The hash in `middleware.ts` has drifted from the served script. Block release. |
| U7.3 | DevTools → emulate "JavaScript disabled". Reload. | Page renders. Theme falls back to `light` (per DM-001 D3). No script errors block render. |

## U8 — Keyboard contract

| # | Step | Expect |
|---|---|---|
| U8.1 | Tab to the Theme control from the page top. | Focus lands on the currently selected radio (roving tabindex). Focus ring visible. |
| U8.2 | Arrow Right / Arrow Left. | Focus AND selection move together (canonical WAI-ARIA radio pattern). `aria-checked` updates. Theme applies immediately. |
| U8.3 | Arrow at boundary (Left on **Light**, Right on **Dark**). | Wraps to the other end. |
| U8.4 | Space / Enter. | No-op on already-focused radio (selection already moved with arrow keys). No double-fire. |
| U8.5 | Shift+Tab away, Tab back. | Focus returns to selected radio, not first radio. |

## U9 — Screen-reader contract

Run with NVDA (Firefox) **or** VoiceOver (Safari). One pass is enough.

| # | Step | Expect |
|---|---|---|
| U9.1 | Focus radiogroup. | Group name announced as **"Theme"**. Current selection announced. |
| U9.2 | Arrow through Light → System → Dark. | Each radio name announced. Selection-change announced. |
| U9.3 | Rapid arrow-mashing (5+ in <1s). | SR announcements **throttled to 500ms**. Exactly one of three allowed strings is read: "Theme changed to light" / "Theme changed to dark" / "Theme changed to system (currently *light*|*dark*)". No other phrasing. |
| U9.4 | OS theme flips while `ta.theme === "system"` (U4 scenario). | No SR announcement fires (passive system response is not a user action). |

## U10 — Touch / mobile

| # | Step | Expect |
|---|---|---|
| U10.1 | Viewport ≤ 480px. | Theme control still reachable. Each radio hit-target ≥ **44×44 px** (DM-001 mobile contract; desktop bar is 32px). |
| U10.2 | Tap radios in turn. | Selection updates. No 300ms tap-delay artefact. |
| U10.3 | OS dark-mode toggle from notification shade (real device only, optional). | If `ta.theme === "system"`, live flips. |

## U11 — Reduced motion (sanity)

| # | Step | Expect |
|---|---|---|
| U11.1 | DevTools → emulate `prefers-reduced-motion: reduce`. Switch themes via the toggle. | No motion. (There must be **no** global theme-switch animation regardless of this setting — DM-001 vestibular contract bans it unconditionally.) |
| U11.2 | DevTools → emulate `prefers-reduced-motion: no-preference`. Switch themes. | Still no global theme animation. Per-component micro-transitions (e.g. button hover) may animate normally. |

---

## Sign-off ledger

| Row | Reviewer | Browser | Theme + viewport | Date | ✅/❌ |
|---|---|---|---|---|---|
| U1 | _____ | Chromium-latest | light · desktop | | |
| U1 | _____ | Firefox-latest | light · desktop | | |
| U2 | _____ | Chromium-latest | dark · desktop | | |
| U2 | _____ | Firefox-latest | dark · desktop | | |
| U3 | _____ | Chromium-latest | both · desktop | | |
| U4 | _____ | Chromium-latest | both · desktop | | |
| U5 | _____ | Chromium-latest | both · desktop | | |
| U5 | _____ | Firefox-latest | both · desktop | | |
| U6 | _____ | Chromium-latest | both · desktop | | |
| U7 | _____ | Chromium-latest | either · desktop | | |
| U8 | _____ | Firefox-latest | either · desktop | | |
| U9 | _____ | NVDA+Firefox **or** VO+Safari | either · desktop | | |
| U10 | _____ | Chromium DevTools mobile emu | both · ≤480px | | |
| U11 | _____ | Chromium-latest | either · desktop | | |
| Safari spot-check | _____ | Safari-latest | both · desktop | | |

**Release gate:** All rows ✅ before tagging `v0.5.0`. Any ❌ on U1–U7 or U9 is a release blocker. ❌ on U8 / U10 / U11 / Safari spot is a follow-up issue, **not** a blocker, provided it is filed before tag-cut.

## Failure-routing cheatsheet

| Symptom | Owner |
|---|---|
| Flash on first paint (U1/U2/U6) | application-development-squad — no-FOUC IIFE drifted or moved below a color-emitting stylesheet |
| `data-theme="system"` ever seen in DOM | application-development-squad — D1 DOM-signal contract violated |
| CSP violation on the inline IIFE (U7.2) | security-hardening-squad — hash in `middleware.ts` ≠ served script byte-hash |
| Contrast row visually fails on a real surface | experience-design-squad — token usage drift; consult `dark-mode-tokens.md` contrast matrix |
| SR announces a phrase other than the three allowed (U9.3) | experience-design-squad + application-development-squad — copy regressed |
| Storage key isn't `ta.theme` | application-development-squad — DM-002 v2 contract violated; v1 key `ta:theme:v1` no longer used post-eef7251 |

---

## Notes

- This file complements, **does not replace**, `MERGE-RUNBOOK.md` §5. Both must pass.
- The contrast matrix in `docs/design/dark-mode-tokens.md` (commit `8a17db7` on `xd/dm-001-contrast-matrix`) is the analytical companion to U5; UX-SMOKE is the perceptual companion.
- If U1–U11 surface a **token-name** regression, that is a public-API break — escalate to experience-design-squad before patching, do not rename ad-hoc.
- Telemetry verification (DM-006 follow-up) is **out of scope** for this checklist and lives in the deferred telemetry workstream.
