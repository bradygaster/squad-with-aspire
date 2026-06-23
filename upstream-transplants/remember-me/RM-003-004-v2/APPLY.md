# RM-003/004 v2 — Cookie-bound refresh + Family + CSRF

**Source:** application-development-squad, commit `245e6d0` on `feature/rm-003-rm-004-remember-me`
**Parent:** `2760901` (v1, MERGE-BLOCKED for D5-3 violation — refresh in JS storage)
**Target:** `tamirdresher/travel-assistant@feature/remember-me` (PR #30)
**Squash subject:** `feat(auth): remember-me checkbox + cookie-bound refresh — RM-003/004/005`

## What changed vs v1 (2760901)

| Concern | v1 (BLOCKED) | v2 (this patch) |
|---|---|---|
| D5-3 refresh location | `localStorage` via `tokenStorage.ts` | `Set-Cookie: ta_rt; HttpOnly; Secure; SameSite=Lax; Path=/api/auth` (exact, no trailing slash) |
| D5-2 unchecked TTL | 1d | **8h sliding** (28800s) |
| D5-2 checked TTL | 30d only | **30d sliding + 90d absolute cap** (`FamilyOriginAt`) |
| D5-4 family | absent | `FamilyId` minted at login, preserved on rotation; reuse → `RevokeFamilyAsync` |
| D5-5 logout | n/a | `/api/auth/logout` revokes current family |
| §5 CSRF | absent | `/refresh` + `/logout` require `X-TA-Refresh: 1` → 403 otherwise |
| §8 trust | from request body | `RememberMe` read from DB row on `/refresh` |
| Setter | `rememberMeStorage.ts` | `setRememberMe.ts` (matches semgrep exclusion path) |
| Vitest | n/a | **9/9 green** (setter, Safari private, credentials:include, X-TA-Refresh, anti-token-in-JS-storage) |
| xUnit | 5 tests on v1 contract | **NOT UPDATED — out of scope (app-dev's local lacks .NET 9 runner)**; follow-up required on CI |

## Files (9 changed, +346/-220)

Apply order: standard `git am` against `feature/remember-me` tip `45991d9`.

## How to apply on travel-assistant

```powershell
cd C:\Users\tamirdresher\source\repos\travel-assistant
git checkout feature/remember-me
git pull
git am C:\Users\tamirdresher\source\repos\squad-with-aspire\upstream-transplants\remember-me\RM-003-004-v2\0001-RM-003-RM-004-v2-cookie-family-csrf.patch
$tok = gh auth token --user tamirdresher
git push "https://x-access-token:$tok@github.com/tamirdresher/travel-assistant.git" feature/remember-me
```

## Gate expectations

- `remember-me-gate.yml` v2 (08c35f3) contract-invariants → should pass (no `tokenStorage.ts`, no `ta_refresh`, no `43200`, cookie path `Path=/api/auth` exact, `X-TA-Refresh` present, `FamilyId` column present, `setRememberMe.ts` is sole writer of `ta.auth.rememberMe`).
- `threat-model-approved` → already green (`RM-005: APPROVED` marker present from aee02f4).
- `build-and-test` (node 20+22 + dotnet RememberMe filter) → vitest 9/9 green; **xUnit 5 tests will FAIL** against v2 contract per app-dev note. Follow-up needed before merge.

## Open follow-up

- xUnit suite update against cookie/family/CSRF contract before squash-merge. Hand to app-dev once .NET 9 runner is available, or rev-deploy can rewrite if blocking.
