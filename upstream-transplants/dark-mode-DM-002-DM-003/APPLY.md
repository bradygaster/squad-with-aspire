# DM-002 + DM-003 — Maintainer One-Shot Apply

**Target repo:** `tamirdresher/travel-assistant`
**Target branch:** `dm-001-design-tokens` (same integration branch as DM-001 + DM-005)
**Source commit (app-dev, local):** `eef7251` ⚠️ **UPDATED** — v2 storage-key rev, NOT the original `b831f26`. Apply this commit only.
**Source branch (app-dev, local):** `feature/dm-002-dm-003-theme-toggle`
**Commit sequence on branch:** `b831f26 → 4d875a2 → 460caf9 → eef7251 → 9fd96dc` (DM-006 sec-hard CSP wiring builds on top — see §"Optional DM-006 cherry-pick" below).
**Patchset location (app-dev session):** `squads/application-development/.squad/session-state/c93377a2-69b8-44a1-9d56-b5678c3665ce/files/0001-DM-002-DM-003-storage-key-v2.patch` (12,289 B, 9 files, +26/−26, applies cleanly on `460caf9` via `git am`).

EMU blocks `tamirdresher_microsoft` from pushing to `tamirdresher/travel-assistant`. Maintainer with non-EMU push rights applies this bundle.

---

## Why this is a transplant (not a fork PR)

Same EMU wall as DM-001 / DM-005 / squad #1372. App-dev built the patch locally, can't push. The two artifacts above are byte-stable and contain the full 10-file diff. This APPLY.md is the maintainer-facing companion; the actual bytes live in app-dev's session state and should be pulled from there at apply time (or requested inline from app-dev via planning if the session has been cleared).

## What the patch lands (9 files)

| Path | Op | Notes |
|---|---|---|
| `apps/web/src/theme/ThemeProvider.tsx` | NEW | Context `{ choice, resolved, setChoice }`. Hydrates via `storage.getThemeChoice()`. Subscribes to `matchMedia('(prefers-color-scheme: dark)')` only when `choice==='system'`. Emits `theme.changed` CustomEvent `{from,to,source}`. Re-exports `THEME_STORAGE_KEY`, `ThemeChoice`, `ResolvedTheme` from `./types`. |
| `apps/web/src/theme/ThemeToggle.tsx` | NEW | `role="radiogroup"` w/ 3 `sr-only` radios (Light/System/Dark). Consumes `useTheme().setChoice`. Markup is stub — XD's `dark-mode-tokens.md` + `toggle.md` provide the final class names (DM-001 commit `ac1fae9`). |
| `apps/web/src/theme/noFoucScript.ts` | NEW | 355-byte inline IIFE (under 500-B budget; grew from 352 → 355 B in v2 because key string `ta:theme:v1` is 3 bytes longer than `ta.theme`). try/catch around `localStorage` + `matchMedia`. Fail-closed to `light`. Exported as `NO_FOUC_SCRIPT` const → SHA-256 is deterministic, see CSP note below. |
| `apps/web/src/theme/__tests__/noFoucScript.test.ts` | NEW | 8 vitest cases × `{stored=light,dark,system} × {mm=dark,light} + garbage + storage-throws + missing-matchMedia`. |
| `apps/web/src/theme/types.ts` | MOD (v2) | `THEME_STORAGE_KEY = 'ta:theme:v1'` constant (locked by release-captain). |
| `apps/web/src/theme/storage.ts` | MOD (v2) | Consumes `THEME_STORAGE_KEY`. Sec-hard's DM-005 module; v2 rev included in this patch — sec-hard didn't need to touch again. |
| `apps/web/src/theme/setTheme.ts` | MOD (v2) | Consumes `THEME_STORAGE_KEY`. |
| `apps/web/src/security/theme-boot-hash.generated.ts` | MOD (v2) | Auto-regenerated: `THEME_BOOT_SHA256_B64 = 'xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8='`. Consumed by sec-hard's CSP middleware (DM-006). |
| `apps/web/src/theme/__tests__/ThemeProvider.test.tsx` | MOD (v2) | Storage-key string updated in test setup. |
| `apps/web/src/theme/__tests__/ThemeToggle.test.tsx` | MOD (v2) | Storage-key string updated in test setup. |
| `apps/web/vitest.config.ts` | NEW | jsdom env. |
| `apps/web/vitest.setup.ts` | NEW | RTL cleanup + `data-theme` reset between tests. |
| `apps/web/src/app/layout.tsx` | MOD | Wraps children in `<ThemeProvider>`. Injects `<script dangerouslySetInnerHTML={{ __html: NO_FOUC_SCRIPT }} />` in `<head>` before stylesheet. Mounts `<ThemeToggle>` top-right. Adds `suppressHydrationWarning` on `<html>`. |
| `apps/web/src/app/globals.css` | MOD | Tailwind v4: switches `dark:` variant from media query to `@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *))`. Hoists dark tokens under `[data-theme="dark"]`. |
| `apps/web/package.json` | NOT in v2 patch | Vitest deps already landed in earlier commit `4d875a2`. No re-bump in this patch. |
| `apps/web/pnpm-lock.yaml` | NOT in patch | Run `pnpm install` after apply. |

## Contracts honored

- **DM-001** (commit `ac1fae9`): `globals.css` uses the `[data-theme="dark"]` selector contract. Token CSS custom-prop names from `docs/design/dark-mode-tokens.md` are consumed verbatim (no renames).
- **DM-005** (commit `e193c39`): All writes route through `storage.setThemeChoice()`. **Zero raw `localStorage.setItem('ta:theme:v1', …)` calls** — semgrep `auth-ui-token-hygiene` + dark-mode-gate `contract-invariants` job will both stay green. v2 rev (`eef7251`) bumped the key from `ta.theme` → `ta:theme:v1` to match release-captain lock.
- **Telemetry boundary (DM-006 deferred):** No App Insights / OTel emission. Browser-only `theme.changed` CustomEvent is the integration seam if/when az-infra wires the web OTel pipeline.

## CSP coordination (open item from app-dev → sec-hard)

No-FOUC ships as inline `<script>`. Two options for a strict CSP without `'unsafe-inline'`:

- **(recommended) SHA-256 hash in `script-src`** — `NO_FOUC_SCRIPT` is a build-time-stable string, so the hash is deterministic. **v2 hash:** `sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8=` (script grew 352 → 355 B with longer key). Zero runtime cost. Add the hash to whatever CSP middleware DM-005 lands. **NOTE (DM-006 landed):** sec-hard's commit `9fd96dc` already wires this into `apps/web/middleware.ts` + `apps/web/src/security/csp.ts` consuming the regenerated `THEME_BOOT_SHA256_B64` constant. Cherry-pick `9fd96dc` together with `eef7251` to land CSP in the same merge train (see §"Optional DM-006 cherry-pick" below).
- (alternative) Per-request nonce via Next.js middleware. Higher complexity, no benefit here.

**Action item for sec-hard:** if DM-005 ships a CSP, include the SHA-256 of `NO_FOUC_SCRIPT` in `script-src`. App-dev exports the constant from `apps/web/src/theme/noFoucScript.ts` precisely to make this trivial.

## Maintainer one-shot

```pwsh
# 1. Clone or update local checkout of travel-assistant on a non-EMU account.
cd path\to\travel-assistant
git fetch origin
git checkout dm-001-design-tokens
git pull --ff-only

# 2. Apply app-dev's v2 patch (eef7251).
git am path\to\app-dev\session\files\0001-DM-002-DM-003-storage-key-v2.patch

# 2b. (Optional) cherry-pick DM-006 sec-hard CSP wiring on top.
#     See §"Optional DM-006 cherry-pick" below.

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
  2. Click Light → `<html data-theme="light">`, `localStorage['ta:theme:v1']='light'`, no flash.
  3. Reload → still light, no flash.
  4. Click System → tracks `prefers-color-scheme` again.
  5. Dispatch `window.addEventListener('theme.changed', e => console.log(e.detail))` → fires on each change.
- a11y: tab into the radiogroup, arrow keys move selection, screen reader announces "Light radio button selected / 1 of 3" etc.

## Branch protection / required checks (post-merge to main)

When the integration branch lands on `main` via PR #28, the dark-mode-gate workflow at `.github/workflows/dark-mode-gate.yml` (per release-captain bundle `eb6b8c5`) is the required check. Specifically, the `gate` aggregator job must be green; it depends on:

- `contract-invariants` — theme union, storage key `ta:theme:v1` (v2 patch `eef7251` aligned with release-captain lock; gate passes),
- `build-and-test` — pnpm install / lint / typecheck / test / build on node 20,
- `csp-and-threat-model` — requires `DM-005: APPROVED` marker in `docs/dark-mode/threat-model.md`.

## ✅ Storage-key reconcile (RESOLVED in v2)

Release-captain v0.5.0 bundle (squad-with-aspire@`eb6b8c5`) locked storage key as **`ta:theme:v1`**. App-dev's original `b831f26` used `ta.theme`. **v2 commit `eef7251` renames across all 9 files (5 source + 3 test + 1 generated hash), +26/−26.** Contract-invariants gate now passes. Full rationale + diff fragments preserved in `RECONCILE-storage-key.md` (sibling file) for audit.

**Reviewer byte-verify (DM-003 gate #3):**

```pwsh
node -e "console.log(require('crypto').createHash('sha256').update(require('fs').readFileSync('apps/web/src/theme/noFoucScript.ts','utf8').match(/`([\s\S]*?)`/)[1]).digest('base64'))"
```
Expected: `xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8=`

## Optional DM-006 cherry-pick (CSP middleware, sec-hard `9fd96dc`)

Sec-hard's DM-006 commit `9fd96dc` (on `feature/dm-002-dm-003-theme-toggle`, parent `eef7251`) wires the boot-script SHA into a strict CSP. Adds 4 files, +275 LOC:

- `apps/web/src/security/csp.ts` — `buildCsp()` consumes `THEME_BOOT_CSP_SOURCE` from the generated hash module. Emits `script-src 'self' 'sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8='` plus 9 other directives + `SECURITY_HEADERS` block.
- `apps/web/middleware.ts` — Next.js edge middleware. Enforces CSP in prod, `Content-Security-Policy-Report-Only` in dev/preview. Matcher skips `_next/static` + static assets.
- `apps/web/src/security/__tests__/csp.test.ts` — 8 vitest assertions (no `unsafe-inline`, no `unsafe-eval`, no `data:`/`blob:` in script-src, hash present, `frame-ancestors 'none'`, etc.).
- `.semgrep/dark-mode-storage.yml` — +2 ERROR rules scoped to `security/csp.*`, `middleware.*`, `next.config.*`.

**Recommendation:** cherry-pick `9fd96dc` together with `eef7251` for the v0.5.0 merge train so CSP rollout is no longer "deferred". DM-005 §8 sign-off contract items 1–5 then all enforce by code + tests + semgrep. If you prefer the v0.5.0 scope to stay strictly DM-001/002/003/005, apply `eef7251` alone and open a follow-up PR for `9fd96dc`.

After cherry-pick: `pnpm test` covers 27 (theme) + 8 (csp) = 35 cases.

## Sign-off

- App-dev (`eef7251`, supersedes `b831f26`): code + 8 unit tests, no-FOUC 355 B ≤ 500 B budget honored; v2 storage-key rev aligned with release-captain lock.
- XD (`ac1fae9`): tokens + wireframe contracts consumed.
- Sec-hard (`e193c39` storage + `9fd96dc` DM-006 CSP wiring, optional cherry-pick): storage gate + threat model + strict CSP all in place.
- QT: DM-004 contract suite at squad-with-aspire@`c6b3de4` runs against this implementation (21 tests green incl. negative assertion against `ta.theme` leak).
- Az-infra: N/A for v0.5.0 (DM-006 deferred for telemetry only — squad-with-aspire@`ed160de`; sec-hard's `9fd96dc` is CSP, not telemetry).
- Review-deployment (this doc): merge gate `.github/workflows/dark-mode-gate.yml` (squad-with-aspire@`eb6b8c5`); v2 storage-key rev clears the prior blocker.
