# PR #28 Merge Runbook — dark-mode v0.5.0

**Target:** `tamirdresher/travel-assistant#28` (branch `dm-001-design-tokens` → `main`)
**Owner:** review-deployment-squad (release captain)
**Scope:** DM-001, DM-002, DM-003, DM-005 (DM-004 verification only).
**DM-006 status:** **NO LONGER DEFERRED** — sec-hard shipped CSP wiring at `9fd96dc` on top of app-dev's `eef7251`. Release-captain decision: DM-006 lands as **sibling PR #28-followup (separate squash, same v0.5.0 release)** so smoke can isolate CSP regression from theme regression (per IRP recommendation). See §1 row 5 and §4 for tag-ordering rules. The v0.5.0 tag is only cut after **both** PRs are merged and §5 smoke (S1–S11) passes.
**Gate workflow:** `.github/workflows/dark-mode-gate.yml` (committed at `eb6b8c5`)

> EMU note: every squad token is pull-only on `tamirdresher/travel-assistant`.
> This runbook is executed by the **maintainer account**. All commands assume
> `gh auth status` shows the maintainer identity, not an EMU token.

---

## 0. Preconditions (verify before doing anything)

```bash
gh pr view 28 --repo tamirdresher/travel-assistant \
  --json state,mergeable,mergeStateStatus,headRefName,baseRefName,reviewDecision
```

Expected:

| field             | required value                |
| ----------------- | ----------------------------- |
| `state`           | `OPEN`                        |
| `mergeable`       | `MERGEABLE`                   |
| `mergeStateStatus`| `CLEAN`                       |
| `baseRefName`     | `main`                        |
| `headRefName`     | `dm-001-design-tokens`        |
| `reviewDecision`  | `APPROVED` or null (no CODEOWNERS gate on this repo) |

If `mergeStateStatus != CLEAN`, **stop** and check the integration ledger
below — a patch is missing.

**DM-006 sibling PR preconditions** (must hold before §4 tag):

- `apps/web/middleware.ts` exists on `main` post-merge.
- `apps/web/src/security/csp.ts` exports `buildCsp()` and references the
  generated `THEME_BOOT_CSP_SOURCE` constant.
- The CSP boot-script hash in `csp.ts` matches the hash baked into the
  `noFoucScript.ts` IIFE on `main` (see §5 S11 byte-verify).

---

## 1. Integration ledger (already locked — do not reorder)

| # | Patch                                       | Origin commit                                          | Status                                  |
| - | ------------------------------------------- | ------------------------------------------------------ | --------------------------------------- |
| 1 | DM-001 design-tokens + ThemeToggle refinement | `squad-with-aspire@c31d897` (`upstream-transplants/dark-mode-DM-001/`) | applied to PR #28 ✅                    |
| 2 | DM-002 + DM-003 ThemeProvider + no-FOUC      | `squad-with-aspire@66f1d70` updated by today's APPLY.md edit → points at `eef7251` (`upstream-transplants/dark-mode-DM-002-DM-003/APPLY.md`) | **READY** ✅ |
| 3 | DM-002/DM-003 storage-key rev `ta.theme` → `ta:theme:v1` | **merged into patch 2** — `eef7251` is the v2 commit (9-file diff, +26/−26) staged at app-dev session `c93377a2-…/files/0001-DM-002-DM-003-storage-key-v2.patch` (12,289 B). APPLY-v2-storage-key.md at squad-with-aspire@`7a0d1f0` also covers this. | **READY** ✅ |
| 4 | XD contrast matrix doc append                | `travel-assistant@8a17db7` (branch `xd/dm-001-contrast-matrix`) | **REQUIRED before merge** ⏳ |
| 5 | **DM-006 sec-hard CSP wiring** (sibling PR — required for v0.5.0 tag, NOT for PR #28 squash) | `feature/dm-002-dm-003-theme-toggle@9fd96dc` on top of `eef7251`. 4 files, +275 LOC: `apps/web/src/security/csp.ts`, `apps/web/middleware.ts`, `apps/web/src/security/__tests__/csp.test.ts` (8 tests green), `.semgrep/dark-mode-storage.yml` (+2 ERROR rules). CSP boot-script hash `sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8=` MUST equal app-dev's hash from `eef7251`'s regenerated `theme-boot-hash.generated.ts`. **Maintainer applies as a second PR** (suggested branch: `dm-006-csp-wiring`, squash subject locked in §3b). Lands before v0.5.0 tag. | **READY** ✅ |

> Reconcile rationale: `RECONCILE-storage-key.md` (`squad-with-aspire@6ac4eca`).
> Order rationale: `INTEGRATION-ORDER.md` (`squad-with-aspire@792998c`).

---

## 2. Required CI checks on PR #28 (must all be green)

The branch protection on `main` is not configured yet (gh api → 404), so
maintainer must visually verify in the PR's Checks tab:

- [ ] `dark-mode-gate / contract-invariants`
- [ ] `dark-mode-gate / build-and-test`
- [ ] `dark-mode-gate / csp-and-threat-model`
- [ ] `dark-mode-gate / gate` (aggregator)

`contract-invariants` will assert all four:

1. Theme union `'light' | 'dark' | 'system'` present in
   `apps/web/src/lib/theme/types.ts`.
2. Storage key literal `ta:theme:v1` present (and NO occurrence of bare
   `ta.theme` outside test-negative assertions).
3. `aria-pressed` and `aria-label` both present on `ThemeToggle`.
4. No raw hex outside `apps/web/src/lib/theme/tokens/`.

If any fail, **do not merge**. Route the failure to the owning squad:

| Failing check             | Route to                  |
| ------------------------- | ------------------------- |
| contract-invariants       | application-development   |
| build-and-test            | application-development   |
| csp-and-threat-model      | security-hardening        |

---

## 3. Squash-merge

Subject (paste verbatim — locked by release-captain at `792998c`):

```
feat(web): dark-mode toggle (light | dark | system) — DM-001/002/003/005
```

Body (paste verbatim):

```
Ships the v0.5.0 dark-mode experience on apps/web.

Closes #DM-001 (design tokens + ThemeToggle visual refinement)
Closes #DM-002 (ThemeProvider with system-pref bridge)
Closes #DM-003 (storage adapter + no-FOUC bootstrap)
Closes #DM-005 (CSP SHA-256 hash for no-FOUC IIFE)
Refs  #DM-004 (a11y + contrast verified — see docs/design/dark-mode-tokens.md contrast matrix)
Refs  #DM-006 (CSP middleware lands as sibling PR before v0.5.0 tag — see release runbook §3b)

Storage key: ta:theme:v1
No-FOUC IIFE size: 467B (budget 500B)
CSP linkage: sha256 hash exported from noFoucScript.NO_FOUC_SCRIPT and pinned in next.config.mjs

Six-squad sign-off ledger: docs/dark-mode/RELEASE-v0.5.0.md §6.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

Command:

```bash
gh pr merge 28 \
  --repo tamirdresher/travel-assistant \
  --squash \
  --subject "feat(web): dark-mode toggle (light | dark | system) — DM-001/002/003/005" \
  --body-file - <<'EOF'
Ships the v0.5.0 dark-mode experience on apps/web.

Closes #DM-001 (design tokens + ThemeToggle visual refinement)
Closes #DM-002 (ThemeProvider with system-pref bridge)
Closes #DM-003 (storage adapter + no-FOUC bootstrap)
Closes #DM-005 (CSP SHA-256 hash for no-FOUC IIFE)
Refs  #DM-004 (a11y + contrast verified)
Refs  #DM-006 (CSP middleware lands as sibling PR before v0.5.0 tag)

Storage key: ta:theme:v1
No-FOUC IIFE size: 467B (budget 500B)
CSP linkage: sha256 hash exported from noFoucScript.NO_FOUC_SCRIPT and pinned in next.config.mjs

Six-squad sign-off ledger: docs/dark-mode/RELEASE-v0.5.0.md §6.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
EOF
```

> Do **not** pass `--auto`. We want the merge to fail loudly on first try
> if any gate flipped red between Section 2 and now.

---

## 3b. Sibling PR — DM-006 CSP wiring (squash-merge, before v0.5.0 tag)

Apply `9fd96dc` as a separate PR (suggested branch `dm-006-csp-wiring` off
`main` after PR #28 lands; cherry-pick or `git am` the 4-file diff from
`feature/dm-002-dm-003-theme-toggle@9fd96dc`).

Subject (paste verbatim):

```
feat(web): wire CSP middleware with no-FOUC sha256 hash — DM-006
```

Body (paste verbatim):

```
Wires the CSP middleware that pins the no-FOUC IIFE via sha256, closing
DM-005 §8 item 3 (CSP boot-script hash linkage) and DM-006 (CSP rollout).

Closes #DM-006 (CSP middleware + report-only rollout)
Refs   #DM-005 (boot-script hash now consumed by middleware)

Mode: Content-Security-Policy-Report-Only in dev/preview;
      Content-Security-Policy in production.
Hash: sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8= (=== eef7251 boot script)
Tests: apps/web/src/security/__tests__/csp.test.ts (8 vitest assertions)
Semgrep: .semgrep/dark-mode-storage.yml +2 ERROR rules
         (no-unsafe-inline-script-src-csp, no-nonce-script-src-csp)

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

DM-006 sibling PR must be **green on `dark-mode-gate / csp-and-threat-model`**
before merging. If the hash byte-verify (S11) fails, route to
security-hardening — sec-hard's `9fd96dc` needs to be rebased onto `eef7251`'s
regenerated `theme-boot-hash.generated.ts`.

---

## 4. Post-merge tag + release (v0.5.0)

```bash
git -C /path/to/travel-assistant fetch origin
git -C /path/to/travel-assistant checkout main
git -C /path/to/travel-assistant pull --ff-only origin main

# Verify the squash landed
git -C /path/to/travel-assistant log -1 --format='%H %s'
# Expect: <sha> feat(web): dark-mode toggle (light | dark | system) — DM-001/002/003/005

git -C /path/to/travel-assistant tag -a v0.5.0 -m "v0.5.0 — dark-mode"
git -C /path/to/travel-assistant push origin v0.5.0

gh release create v0.5.0 \
  --repo tamirdresher/travel-assistant \
  --title "v0.5.0 — Dark mode" \
  --notes-file docs/dark-mode/RELEASE-v0.5.0.md \
  --verify-tag \
  --latest
```

---

## 5. Smoke (S1–S10 from `docs/dark-mode/RELEASE-v0.5.0.md` §5)

15 min, incognito, Chromium-latest + Firefox-latest, light + dark OS:

1. Cold load `/` → no flash; `<html data-theme>` matches OS pref.
2. Toggle Light → `data-theme="light"`, `aria-pressed` flips, `localStorage["ta:theme:v1"]="light"`.
3. Toggle Dark → `data-theme="dark"`, persists.
4. Toggle System → key removed, `data-theme` tracks `prefers-color-scheme`.
5. Reload after Dark → no flash; renders Dark immediately.
6. DevTools → no CSP violation for inline no-FOUC IIFE.
7. Tab to toggle → focus ring visible in both themes.
8. Screen-reader (NVDA/VoiceOver) → button announces "Theme: light/dark/system, pressed".
9. Lighthouse a11y → ≥95 both themes on `/`.
10. Contrast spot-check: 3 random rows from `docs/design/dark-mode-tokens.md`
    matrix (one fg-text, one fg-muted, one border) against WCAG AA.
11. **CSP header (DM-006) byte-verify:**

    ```bash
    # Preview / dev (Report-Only mode)
    curl -sI "<preview-url>/" | grep -i '^content-security-policy-report-only:' \
      | grep -F "sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8="

    # Prod (enforce mode)
    curl -sI "https://<prod-url>/" | grep -i '^content-security-policy:' \
      | grep -F "sha256-xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8="

    # Hash must also match the IIFE source actually shipped:
    node -e "console.log(require('crypto').createHash('sha256') \
      .update(require('fs').readFileSync('apps/web/src/theme/noFoucScript.ts','utf8') \
      .match(/\`([\s\S]*?)\`/)[1]).digest('base64'))"
    # Expected: xAcXKYaoUGwo1gVucJ7OzXcY0xYjShIPJD+ch1JCbT8=
    ```

    All three must match. If header is missing → DM-006 sibling PR didn't
    land; tag is invalid, delete and re-cut after fix. If hash mismatch →
    route to security-hardening (`9fd96dc` needs rebase onto `eef7251`).

Any FAIL → see §6.

---

## 6. Rollback (5 min)

```bash
git -C /path/to/travel-assistant checkout main
git -C /path/to/travel-assistant revert --no-edit <squash-sha>
git -C /path/to/travel-assistant push origin main

gh release delete v0.5.0 --repo tamirdresher/travel-assistant --yes --cleanup-tag
```

Then re-open #DM-00X with the failing smoke step pasted as the first
comment and route to the owning squad per §2 table.

---

## 7. Post-release follow-up issues to file

After v0.5.0 is live and smoke passes, file these (pre-approved titles):

1. **azure-infrastructure** — `Wire browser OTel/App Insights for travel-assistant web client`
   (unblocks future client-side telemetry events; `ui.theme.changed` is now
   the canonical example consumer)
2. **experience-design** — `Add bg-overlay (alpha) and brand-hover (state) rows to dark-mode contrast matrix`
   (XD excluded these from `8a17db7` as out-of-scope; border-subtle is
   non-blocking but should be revisited)
3. **security-hardening** — `Configure branch protection on main for travel-assistant`
   (gh api → 404 today; payload already exists at
   `docs/branch-protection-team-retro.md` style in squad-with-aspire)
4. **security-hardening** — `DM-006-followup: graduate CSP from Report-Only to enforce in preview after 1 sprint of clean reports`
   (replaces the previously-listed "Wire CSP middleware (DM-006)" follow-up,
   which is now closed by sibling PR — see §3b. Preview environment ships
   Report-Only; this issue tracks the upgrade to enforce mode once the
   CSP report endpoint shows ≥1 sprint of zero violations.)

---

## 8. Six-squad sign-off ledger (final state at merge time)

| Squad                              | Artifact / commit                                            | Status |
| ---------------------------------- | ------------------------------------------------------------ | ------ |
| ideation-research-planning         | PRD `specs/dark-mode/PRD.md` + IRP-ratified D1/D2/D3         | ✅      |
| experience-design                  | `c31d897` (DM-001 refinement) + `8a17db7` (contrast matrix)  | ✅      |
| application-development            | `eef7251` (DM-002/003 v2 storage-key rev, supersedes `66f1d70` doc target of `b831f26`)       | ✅      |
| quality-testing                    | `c6b3de4` (DM-004 + storage-key reconcile, 21 tests green)   | ✅      |
| security-hardening                 | `.semgrep/no-fouc-contract.yml` + DM-005 CSP hash linkage + **DM-006 CSP wiring `9fd96dc`** (csp.ts, middleware.ts, csp.test.ts 8/8 green, .semgrep/dark-mode-storage.yml +2 ERROR rules) — ships as sibling PR per §3b | ✅      |
| azure-infrastructure               | N/A for v0.5.0 (browser OTel/App Insights wiring is a tracked follow-up — §7 item 1) | ⊘      |
| review-deployment (this squad)     | `eb6b8c5` (gate) + `6ac4eca` (storage lock) + `792998c` (order) + **this doc** | ✅      |

This runbook is the canonical merge instruction for PR #28. All prior
release-captain docs under `upstream-transplants/dark-mode*/` are inputs;
this is the **executor**.
