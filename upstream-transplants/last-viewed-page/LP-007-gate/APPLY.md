# APPLY — LP-007 last-page-gate

**Owner:** review-deployment-squad
**Target repo:** `tamirdresher/travel-assistant`
**Target branch:** `feature/last-viewed-page` (create from `main` if missing)
**File destination:** `.github/workflows/last-page-gate.yml`

## One-shot (maintainer, from a clone of tamirdresher/travel-assistant)

```powershell
$tok = gh auth token --user tamirdresher
$env:GH_TOKEN = $tok

git fetch origin
git switch -c feature/last-viewed-page origin/main 2>$null
git switch feature/last-viewed-page

New-Item -ItemType Directory -Force .github\workflows | Out-Null
Copy-Item <path-to>\upstream-transplants\last-viewed-page\LP-007-gate\last-page-gate.yml `
          .github\workflows\last-page-gate.yml

git add .github/workflows/last-page-gate.yml
git commit -m "ci(nav): last-page-gate — contract invariants + threat-model + test-suite — LP-007

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"

git push "https://x-access-token:$tok@github.com/tamirdresher/travel-assistant.git" feature/last-viewed-page

gh pr create --repo tamirdresher/travel-assistant `
  --base main --head feature/last-viewed-page `
  --title 'feat(web): remember last viewed page on app reopen — LP-*' `
  --label enhancement `
  --body "Squash target for LP-002/003/005/006. Gate: .github/workflows/last-page-gate.yml. No release tag — rides next routine deploy."
```

## Byte-verify

```powershell
Get-FileHash -Algorithm SHA256 .github\workflows\last-page-gate.yml
# Must match upstream-transplants\last-viewed-page\LP-007-gate\last-page-gate.yml
```

## Notes

- Lands **scaffold + gate only** — gate will FAIL until LP-001/002/003/005/006 land (expected).
- Once all sibling LP patches land and gate flips green, squash-merge with subject:
  `feat(web): remember last viewed page on app reopen — LP-002/003/005/006`
- No release tag. No az-infra work. Pure client-side feature.
