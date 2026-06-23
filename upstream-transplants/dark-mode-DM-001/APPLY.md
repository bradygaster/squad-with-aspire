# DM-001 Refinement Transplant — XD `a124b82` onto travel-assistant `dm-001-design-tokens`

**Source squad:** experience-design-squad  
**Source commit:** `a124b82` on local branch `xd/dm-001-dark-mode-design`  
**Target repo:** tamirdresher/travel-assistant  
**Target branch:** `dm-001-design-tokens` (the existing PR #28 feature branch)  
**Routed by:** review-deployment-squad (EMU-blocked from direct push to travel-assistant)  
**Date:** 2026-06-23

## Why a transplant?

Every squad's EMU token (`tamirdresher_microsoft`) has pull-only on
`tamirdresher/travel-assistant`. Same wall hit on bradygaster/squad #1314, #1372,
and DM-005. Established pattern: stage byte-stable files under
`upstream-transplants/` in this safety mirror, maintainer applies via Contents
API or local push.

## Reconciliation with PR #28

PR #28 is **OPEN** on branch `dm-001-design-tokens` with both target files
already present at older/shorter byte-sizes:

| File | PR #28 size | XD refinement size | Action |
| --- | --- | --- | --- |
| `docs/design/dark-mode-tokens.md` | 4987 B | **8492 B** | Replace |
| `docs/wireframes/dark-mode/toggle.md` | 4922 B | **11456 B** | Replace |

XD's `a124b82` is the **same DM-001 scope re-shipped as a refinement** —
IRP-ratified D1/D2/D3 decisions + locked CSS-custom-property contracts +
a11y radiogroup pattern + initial-paint divergence table + handoff
matrix. The branch name divergence (`xd/dm-001-dark-mode-design` vs
`dm-001-design-tokens`) is purely a local-naming artifact; landing the
bytes on the existing PR #28 branch keeps sec-hard's DM-005 work
(`.semgrep/dark-mode-storage.yml`, `ThemeProvider.tsx`, `ThemeToggle.tsx`,
`no-fouc-contract.md`) on the same feature line.

**Do NOT close PR #28.** Update it in place.

## One-shot apply (maintainer, push-capable)

From a clone of `tamirdresher/travel-assistant` with push rights:

```bash
git fetch origin dm-001-design-tokens
git checkout dm-001-design-tokens

# Copy refined bytes from this transplant bundle (adjust path to your local
# checkout of bradygaster/squad-with-aspire):
TRANSPLANT="../squad-with-aspire/upstream-transplants/dark-mode-DM-001/files"
cp "$TRANSPLANT/docs/design/dark-mode-tokens.md"           docs/design/dark-mode-tokens.md
cp "$TRANSPLANT/docs/wireframes/dark-mode/toggle.md"       docs/wireframes/dark-mode/toggle.md

git add docs/design/dark-mode-tokens.md docs/wireframes/dark-mode/toggle.md
git commit -m "docs(dark-mode): DM-001 refinement — ratified D1/D2/D3 + locked contracts

Refs: XD a124b82, IRP DM-001 ratification, sec-hard e193c39
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"

git push origin dm-001-design-tokens
```

PR #28 picks up the new commit automatically. No new PR needed.

## Verify post-apply

```bash
gh pr view 28 --repo tamirdresher/travel-assistant --json files \
  --jq '.files[] | select(.path | test("dark-mode-tokens|toggle.md")) | "\(.path) +\(.additions) -\(.deletions)"'
```

Expected: both files show net additions reflecting the byte expansion
(roughly 4987→8492 and 4922→11456). PR #28 file count stays at 6.

## Labels

Apply `enhancement` only. `squad:lapid` does **not** exist on this repo
(verified during DM-001 PR #28 — IRP confirmed).

## Files in this bundle

- `files/docs/design/dark-mode-tokens.md` — 8492 B (refined tokens)
- `files/docs/wireframes/dark-mode/toggle.md` — 11456 B (refined toggle wireframes)
- `APPLY.md` — this file

## Downstream unblock

Once landed on `dm-001-design-tokens` branch:

- DM-002 (app-dev) — ThemeProvider/ThemeToggle authoring (PR #28 stubs exist; refinements may require small edits per XD §3-§7)
- DM-003 (app-dev) — No-FOUC script per tokens §5, byte-stable, single CSP `sha256-…` (DM-005 already pinned at sec-hard `e193c39` mirror)
- DM-004 (QT) — Already shipped at squad-with-aspire `b81f74e`
- DM-005 (sec-hard) — Already shipped at squad-with-aspire `e193c39`
- DM-006 (az-infra) — **DEFERRED** in v0.5.0 release notes (squad-with-aspire `ed160de`)

## Release captain note

This is a **refinement-only** transplant — no scope expansion. v0.5.0
critical path (DM-001 → DM-005 → DM-002 → DM-003 → DM-004) and merge gate
(`.github/workflows/dark-mode-gate.yml` per squad-with-aspire `eb6b8c5`)
are unchanged. PR #28 must still pass the dark-mode-gate aggregator before
v0.5.0 ships.
