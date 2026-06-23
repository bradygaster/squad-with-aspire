# Last Viewed Page (LP-*) — Transplant Mirror

**Target:** `tamirdresher/travel-assistant`
**Branch:** `feature/last-viewed-page` (base: `main`)
**Squash subject:** `feat(web): remember last viewed page on app reopen — LP-002/003/005/006`
**Release tag:** none (rides next routine deploy — same model as remember-me, NOT dark-mode v0.5.0)

## Locked invariants (rev-deploy owned — LP-007)

| Contract | Value | Enforced by |
|---|---|---|
| Storage key | `ta.nav.lastPage.v1` (dot-form, version-suffixed) | `last-page-gate.yml` contract-invariants grep |
| Setter path | `apps/web/src/navigation/setLastPage.ts` | Semgrep + grep guard |
| Deny-list module | `apps/web/src/navigation/lastPage.denylist.ts` | Single import source for setter + semgrep + tests |
| Server contract | NONE — purely client-side | No Bicep, no API, no Key Vault |
| Search-param logging | FORBIDDEN | Negative grep on telemetry calls carrying `location.search` |
| Cookie usage | FORBIDDEN for LP — `cookie` literal in nav module fails build | Negative grep |

## Patch landing order (cherry-pick numeric)

| # | Owner | Scope | Status |
|---|---|---|---|
| LP-001 | experience-design | Wireframe + UX copy + denylist authoring | pending |
| LP-002 | application-development | Client persistence (setter + reader hook) | pending |
| LP-003 | application-development | Router boot-restore + race-safe init | pending |
| LP-005 | security-hardening | Threat model `APPROVED` marker + semgrep rule | pending |
| LP-006 | quality-testing | Unit + property + Playwright E2E + smoke matrix | pending |
| LP-007 | review-deployment | `last-page-gate.yml` + this scaffold | **this commit** |

Each LP-00N folder MUST contain:
- `APPLY.md` — maintainer one-shot (commit ref, byte-verify cmd, EMU-bypass push recipe)
- `0001-LP-00N-*.patch` — `git format-patch` artifact, parent SHA pinned

## Maintainer apply order

1. LP-001 (XD) → adds denylist + wireframe doc only, no behavior change
2. LP-005 (sec-hard) in parallel with LP-001 → adds threat-model + semgrep, no app code
3. LP-002 (app-dev) → setter + reader, denylist import in place
4. LP-003 (app-dev) → router wiring
5. LP-006 (QT) → tests
6. LP-007 gate flips green → squash-merge

## EMU bypass (canonical, proven on DM-* / RM-*)

```powershell
$tok = gh auth token --user tamirdresher
git push "https://x-access-token:$tok@github.com/tamirdresher/travel-assistant.git" feature/last-viewed-page
$env:GH_TOKEN = $tok
gh pr create --repo tamirdresher/travel-assistant --base main --head feature/last-viewed-page `
  --title 'feat(web): remember last viewed page on app reopen' --label enhancement
```

`tamirdresher_microsoft` EMU identity is blocked from createPullRequest/push on travel-assistant — use non-EMU `tamirdresher` keyring.
