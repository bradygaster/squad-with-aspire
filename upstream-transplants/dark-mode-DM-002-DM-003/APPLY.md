# DM-002 + DM-003 — Maintainer One-Shot Apply

**Target repo:** `tamirdresher/travel-assistant`
**Target branch:** `dm-001-design-tokens` (same integration branch as DM-001 + DM-005)
**Source commit (app-dev, local):** `b831f26`
**Source branch (app-dev, local):** `feature/dm-002-dm-003-theme-toggle`
**Patchset location (app-dev session):** `.squad/session-state/a19ed6f4-*/files/dm-002-dm-003-pr/`
  - `0001-DM-002-DM-003-...patch` (24,543 B) — `git am`-able
  - `dm-002-dm-003.bundle` (23,286 B) — `git fetch`-able

EMU blocks `tamirdresher_microsoft` from pushing to `tamirdresher/travel-assistant`. Maintainer with non-EMU push rights applies this bundle.

---

## Why this is a transplant (not a fork PR)

Same EMU wall as DM-001 / DM-005 / squad #1372. App-dev built the patch locally, can't push. The two artifacts above are byte-stable and contain the full 10-file diff. This APPLY.md is the maintainer-facing companion; the actual bytes live in app-dev's session state and should be pulled from there at apply time (or requested inline from app-dev via planning if the session has been cleared).

## What the patch lands (10 files)

| Path | Op | Notes |
|---|---|---|
| `apps/web/src/theme/ThemeProvider.tsx` | NEW | Context `{ choice, resolved, setChoice }`. Hydrates via `storage.getThemeChoice()`. Subscribes to `matchMedia('(prefers-color-scheme: dark)')` only when `choice==='system'`. Emits `theme.changed` CustomEvent `{from,to,source}`. Re-exports `THEME_STORAGE_KEY`, `ThemeChoice`, `ResolvedTheme` from `./types`. |
| `apps/web/src/theme/ThemeToggle.tsx` | NEW | `role="radiogroup"` w/ 3 `sr-only` radios (Light/System/Dark). Consumes `useTheme().setChoice`. Markup is stub — XD's `dark-mode-tokens.md` + `toggle.md` provide the final class names (DM-001 commit `ac1fae9`). |
| `apps/web/src/theme/noFoucScript.ts` | NEW | 463-byte inline IIFE (under 500-B budget). try/catch around `localStorage` + `matchMedia`. Fail-closed to `light`. Exported as `NO_FOUC_SCRIPT` const → SHA-256 is deterministic, see CSP note below. |
| `apps/web/src/theme/__tests__/noFoucScript.test.ts` | NEW | 8 vitest cases × `{stored=light,dark,system} × {mm=dark,light} + garbage + storage-throws + missing-matchMedia`. |
| `apps/web/vitest.config.ts` | NEW | jsdom env. |
| `apps/web/vitest.setup.ts` | NEW | RTL cleanup + `data-theme` reset between tests. |
| `apps/web/src/app/layout.tsx` | MOD | Wraps children in `<ThemeProvider>`. Injects `<script dangerouslySetInnerHTML={{ __html: NO_FOUC_SCRIPT }} />` in `<head>` before stylesheet. Mounts `<ThemeToggle>` top-right. Adds `suppressHydrationWarning` on `<html>`. |
| `apps/web/src/app/globals.css` | MOD | Tailwind v4: switches `dark:` variant from media query to `@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *))`. Hoists dark tokens under `[data-theme="dark"]`. |
| `apps/web/package.json` | MOD | Adds `test`/`test:watch` scripts + devDeps: vitest 2.1.8, @vitejs/plugin-react 4.3.4, @testing-library/react 16.1.0, @testing-library/jest-dom 6.6.3, jsdom 25.0.1. |
| `apps/web/pnpm-lock.yaml` | NOT in patch | Run `pnpm install` after apply. |

## Contracts honored

- **DM-001** (commit `ac1fae9`): `globals.css` uses the `[data-theme="dark"]` selector contract. Token CSS custom-prop names from `docs/design/dark-mode-tokens.md` are consumed verbatim (no renames).
- **DM-005** (commit `e193c39`): All writes route through `storage.setThemeChoice()`. **Zero raw `localStorage.setItem('ta.theme', …)` calls** — semgrep `auth-ui-token-hygiene` + dark-mode-gate `contract-invariants` job will both stay green.
- **Telemetry boundary (DM-006 deferred):** No App Insights / OTel emission. Browser-only `theme.changed` CustomEvent is the integration seam if/when az-infra wires the web OTel pipeline.

## CSP coordination (open item from app-dev → sec-hard)

No-FOUC ships as inline `<script>`. Two options for a strict CSP without `'unsafe-inline'`:

- **(recommended) SHA-256 hash in `script-src`** — `NO_FOUC_SCRIPT` is a build-time-stable string, so the hash is deterministic. Zero runtime cost. Add the hash to whatever CSP middleware DM-005 lands.
- (alternative) Per-request nonce via Next.js middleware. Higher complexity, no benefit here.

**Action item for sec-hard:** if DM-005 ships a CSP, include the SHA-256 of `NO_FOUC_SCRIPT` in `script-src`. App-dev exports the constant from `apps/web/src/theme/noFoucScript.ts` precisely to make this trivial.

## Maintainer one-shot

```pwsh
# 1. Clone or update local checkout of travel-assistant on a non-EMU account.
cd path\to\travel-assistant
git fetch origin
git checkout dm-001-design-tokens
git pull --ff-only

# 2. Pull app-dev's patch from their session state (one of):
#    a) Patch:
git am path\to\app-dev\session\files\dm-002-dm-003-pr\0001-DM-002-DM-003-*.patch
#    b) Bundle:
git fetch path\to\app-dev\session\files\dm-002-dm-003-pr\dm-002-dm-003.bundle feature/dm-002-dm-003-theme-toggle
git merge --ff-only feature/dm-002-dm-003-theme-toggle

# 3. Regenerate lockfile + verify.
cd apps\web
pnpm install
pnpm test       # vitest must be green (8 noFoucScript cases + existing storage/ThemeProvider tests)
pnpm typecheck
pnpm lint
pnpm build
cd ..\..

# 4. Push to the existing integration branch (NOT main, NOT a new branch).
git push origin dm-001-design-tokens

# 5. PR #28 already targets main → it now contains DM-001 + DM-005 + DM-002/DM-003.
#    Update PR #28 body to reflect the expanded scope (or open a follow-up PR if you prefer
#    smaller review surface; recommended: keep #28 as the single integration PR per
#    release-deployment-squad's established pattern at squad-with-aspire@eb6b8c5).
```

## PR labels

`enhancement` only. **Do NOT apply `squad:lapid`** — that label does not exist on `tamirdresher/travel-assistant` (verified during DM-001 PR #28 attempt). App-dev's original handoff requested it; this APPLY overrides per repo state.

## Smoke after apply (5 min, before merge)

- `pnpm test` green in `apps/web/`.
- `pnpm build` green.
- Manual browser smoke (incognito):
  1. Load page with system in dark mode → page renders dark, **no flash**.
  2. Click Light → `<html data-theme="light">`, `localStorage['ta.theme']='light'`, no flash.
  3. Reload → still light, no flash.
  4. Click System → tracks `prefers-color-scheme` again.
  5. Dispatch `window.addEventListener('theme.changed', e => console.log(e.detail))` → fires on each change.
- a11y: tab into the radiogroup, arrow keys move selection, screen reader announces "Light radio button selected / 1 of 3" etc.

## Branch protection / required checks (post-merge to main)

When the integration branch lands on `main` via PR #28, the dark-mode-gate workflow at `.github/workflows/dark-mode-gate.yml` (per release-captain bundle `eb6b8c5`) is the required check. Specifically, the `gate` aggregator job must be green; it depends on:

- `contract-invariants` — theme union, storage key `ta:theme:v1` *(note: app-dev's patch uses `ta.theme`; reconcile with sec-hard's DM-005 storage key before merge — see below)*,
- `build-and-test` — pnpm install / lint / typecheck / test / build on node 20,
- `csp-and-threat-model` — requires `DM-005: APPROVED` marker in `docs/dark-mode/threat-model.md`.

## ⚠️ Storage-key reconcile (BLOCKER before merge)

Release-captain v0.5.0 bundle (squad-with-aspire@`eb6b8c5`) locked storage key as **`ta:theme:v1`** (colon separator + version suffix). App-dev's no-FOUC patch references **`ta.theme`** (dot separator, no version). These must match or:

- the no-FOUC script reads the wrong key on first paint → flash,
- the contract-invariants gate job fails.

**DECISION LOCKED (2026-06-23, review-deployment-squad release captain):** `ta:theme:v1` wins. App-dev revs the patch. Full rationale + diff fragments + maintainer apply order in `RECONCILE-storage-key.md` (sibling file).

Maintainer: do **not** apply `b831f26` as-is. Wait for app-dev's revved patchset (subject prefix `fix(dark-mode): rename storage key`). Then apply per the updated order in RECONCILE-storage-key.md §"Maintainer apply order (updated)".

## Sign-off

- App-dev (b831f26): code + 8 unit tests, no-FOUC ≤500 B budget honored.
- XD (ac1fae9): tokens + wireframe contracts consumed.
- Sec-hard (e193c39): storage gate + threat model in place; CSP hash open item flagged above.
- QT: DM-004 contract suite at squad-with-aspire@`b81f74e` runs against this implementation.
- Az-infra: N/A for v0.5.0 (DM-006 deferred — squad-with-aspire@`ed160de`).
- Review-deployment (this doc): merge gate `.github/workflows/dark-mode-gate.yml` (squad-with-aspire@`eb6b8c5`); storage-key blocker called out above.
