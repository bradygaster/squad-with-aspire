# APPLY — DM-002/003 v2 storage-key rev (`ta.theme` → `ta:theme:v1`)

**Status:** READY (blocker cleared). Supersedes the v1 patchset at `upstream-transplants/dark-mode-DM-002-DM-003/APPLY.md` for the storage-key concern only.

**Source:** app-dev commit `eef7251` on branch `feature/dm-002-dm-003-theme-toggle` (parent `460caf9`).
**Patch:** `0002-DM-002-DM-003-v2-rename-storage-key-to-ta-theme-v1-r.patch` (12,289 B, 9 files, +26/-26).
**Patch subject prefix locked:** `fix(dark-mode): rename storage key`.

## Scope (exactly 9 files — no scope creep)

| # | Path | Change |
|---|------|--------|
| 1 | `apps/web/src/theme/types.ts` | `THEME_STORAGE_KEY` constant → `'ta:theme:v1'` |
| 2 | `apps/web/src/theme/storage.ts` | uses constant |
| 3 | `apps/web/src/theme/setTheme.ts` | uses constant |
| 4 | `apps/web/src/theme/noFoucScript.ts` | inline IIFE string literal updated |
| 5 | `apps/web/src/theme/ThemeProvider.tsx` | uses constant |
| 6 | `apps/web/src/theme/__tests__/noFoucScript.test.ts` | test fixtures |
| 7 | `apps/web/src/theme/__tests__/ThemeProvider.test.tsx` | test fixtures |
| 8 | `apps/web/src/theme/__tests__/ThemeToggle.test.tsx` | test fixtures |
| 9 | `apps/web/src/security/theme-boot-hash.generated.ts` | regenerated digest |

**Deliberately excluded** (DM-006 / separate land): `apps/web/src/middleware.ts`, `apps/web/src/security/csp.ts`, `apps/web/pnpm-lock.yaml`.

## New no-FOUC hash (boot script 352B → 355B)

- **sha256 (base64):** `xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8=`
- **CSP directive fragment:** `'sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8='`

The regenerated `theme-boot-hash.generated.ts` exports `THEME_BOOT_SHA256_B64` in lockstep — sec-hard middleware (when it lands under DM-006) consumes the constant, no manual CSP edit needed at apply time.

## Reviewer byte-verify (gate DM-003 #3)

```
cd apps/web
node -e "console.log(require('crypto').createHash('sha256').update(require('fs').readFileSync('src/theme/noFoucScript.ts','utf8').match(/\`([\s\S]*?)\`/)[1]).digest('base64'))"
```

Expected: `xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8=`

## Maintainer one-shot (PR #28 update-in-place)

```bash
# from travel-assistant clone, on branch dm-001-design-tokens (PR #28 head)
git fetch origin
git checkout dm-001-design-tokens

# 1. apply app-dev v2 patch (this doc)
git am /path/to/0002-DM-002-DM-003-v2-rename-storage-key-to-ta-theme-v1-r.patch

# 2. verify
cd apps/web && pnpm install --frozen-lockfile=false && pnpm test
#    → expect 27/27 green

# 3. byte-verify hash (command above)

# 4. push to PR #28
git push origin dm-001-design-tokens
```

After this lands, INTEGRATION-ORDER.md patch 1 = ✅. Next: XD contrast matrix (`8a17db7` on `xd/dm-001-contrast-matrix`, append-only to `docs/design/dark-mode-tokens.md`).

## Gate impact

- `dark-mode-gate.yml::contract-invariants` storage-key assertion (`ta:theme:v1`) → PASS.
- DM-004 QT suite at `c6b3de4` (21 tests incl. negative-assert against `ta.theme` leak) → PASS.
- No-FOUC IIFE size budget 500B: 355B → under budget.

## Ledger update

`upstream-transplants/dark-mode-PR28/INTEGRATION-ORDER.md` patch row:

> Patch 1 = app-dev v2 storage-key rev — **READY** (commit `eef7251`, patch `0002-…v2-rename-storage-key…patch`).

`upstream-transplants/dark-mode-PR28/MERGE-RUNBOOK.md` §1 integration ledger row for patch 2 → flip from ⏳ to **READY** at app-dev commit `eef7251`.

EMU block unchanged. Maintainer apply is the only landing path.
