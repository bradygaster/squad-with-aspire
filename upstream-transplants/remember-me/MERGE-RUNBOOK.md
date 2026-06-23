# remember-me — Merge Runbook (release-deployment squad)

**Target repo:** `tamirdresher/travel-assistant`
**Feature branch:** `feature/remember-me` (release-deployment owns; EMU blocks other squads' direct push)
**Transplant pattern:** mirror under `bradygaster/squad-with-aspire@main:upstream-transplants/remember-me/` (same pattern as dark-mode `eb6b8c5`); maintainer applies one-shot to `tamirdresher/travel-assistant`.
**Release vehicle:** no tag — rides next routine deploy. No `v0.x.0` cut, no `gh release create`.

---

## 0. Preconditions

```pwsh
gh pr view --repo tamirdresher/travel-assistant --json number,headRefName,state,mergeStateStatus <PR#>
gh api repos/tamirdresher/travel-assistant/branches/main/protection  # currently 404 — see follow-up #3
```

Branch protection on `main` still not configured (consistent with dark-mode runbook §7). Required-checks list below is enforced manually by this squad until protection lands.

---

## 1. Integration ledger (4 patches, sequential)

Squads land commits into `bradygaster/squad-with-aspire:upstream-transplants/remember-me/` as patch + APPLY.md pairs. Release-deployment cherry-picks/applies onto `feature/remember-me` in order:

| # | Owner | Scope (RM-id) | Patch artifact path | Status |
|---|---|---|---|---|
| 1 | experience-design | RM-002: checkbox + label + a11y in `LoginForm` | `upstream-transplants/remember-me/RM-002-xd-checkbox/` | ⏳ pending |
| 2 | application-development | RM-003: client persists choice (`ta:auth:remember:v1` localStorage key) + plumbs `remember` bool to `POST /auth/login` | `upstream-transplants/remember-me/RM-003-client-persist/` | ⏳ pending |
| 3 | application-development + azure-infrastructure | RM-004: API issues longer-lived refresh token (30d) when `remember=true`, else 12h; cookie `Max-Age` aligned; key-vault secret rotation note | `upstream-transplants/remember-me/RM-004-api-token-ttl/` | ⏳ pending |
| 4 | security-hardening | RM-005: cookie attrs (`HttpOnly; Secure; SameSite=Lax`), CSRF unchanged, semgrep rule pinning storage key + TTL ceiling, threat-model `RM-005: APPROVED` marker | `upstream-transplants/remember-me/RM-005-sec/` | ⏳ pending |

**Locked invariants** (release-deployment will reject any patch that drifts):

- localStorage key: **`ta:auth:remember:v1`** (version-suffixed, consistent with `ta:theme:v1` precedent — see DM-002/003 RECONCILE).
- Token TTL: `remember=false` → **12h**; `remember=true` → **30d**. No other values.
- Cookie name: `ta_refresh`. Attributes: `HttpOnly; Secure; SameSite=Lax; Path=/api/auth`.
- Revocation triggers (both required): explicit logout, password change.
- No telemetry event in this PR (consistent with DM-006 deferral pattern — separate follow-up #1).

---

## 2. Required gate checks (manual until branch-protection lands)

| Check | Source | Owner |
|---|---|---|
| `pnpm lint` + `pnpm typecheck` + `pnpm test --filter @ta/web` | apps/web | application-development |
| `dotnet test src/TravelAssistant.Api.Tests` (incl. new `RememberMeTokenTtlTests`) | api | application-development |
| `security-static` (semgrep + codeql) | live workflow | security-hardening |
| `auth-ui-contracts-gate` | live (commit `7081a83`) | review-deployment |
| `remember-me-gate` (NEW — see `remember-me-gate.yml` in this directory) | added in this scaffold | review-deployment |

---

## 3. Smoke (4 scenarios — all must pass before squash-merge)

Run incognito in Chromium + Firefox against preview deploy:

1. **Login both branches.** `remember=false` → `ta_refresh` cookie `Max-Age=43200` (12h); `remember=true` → `Max-Age=2592000` (30d). Verify `localStorage['ta:auth:remember:v1']` matches selection.
2. **Refresh both branches.** Reload after `Max-Age/2`. Unchecked path: still authenticated. Checked path: still authenticated. Close browser, reopen after 13h sim → unchecked path requires re-login, checked path still authed.
3. **Logout revokes.** Click logout → refresh-token revoked server-side (next refresh attempt → 401); `ta:auth:remember:v1` cleared; cookie cleared.
4. **Password-change revokes.** Change password while `remember=true` → all outstanding refresh tokens for user invalidated (existing session also forced to re-auth).

---

## 4. Squash-merge subject (paste verbatim)

```
feat(auth): "remember me" checkbox with 30d refresh token — RM-002/003/004/005

Adds a "remember me" checkbox to the login screen. Frontend persists the
choice under localStorage key `ta:auth:remember:v1`; backend issues a 30-day
refresh token when checked (12h otherwise). Cookie `ta_refresh` is HttpOnly,
Secure, SameSite=Lax. Both explicit logout and password change revoke all
outstanding tokens for the user.

Closes #RM-002, #RM-003, #RM-004, #RM-005
Refs: ta:auth:remember:v1 (storage), 30d/12h (TTL ceiling)

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

No release-notes block in this PR body — feature ships via routine deploy.

---

## 5. Rollback (5 min)

```pwsh
gh pr view --repo tamirdresher/travel-assistant <PR#> --json mergeCommit -q .mergeCommit.oid | Set-Variable SHA
git -C travel-assistant revert -m 1 $SHA
git -C travel-assistant push origin main
# Server-side: redeploy previous API image; existing 30d cookies become inert
# because the API will reject `remember` claim. Client localStorage key is
# ignored by reverted client. No data migration required.
```

---

## 6. Pre-approved follow-up issues (file after merge)

1. **`telemetry: emit auth.remember_me.toggled`** — owner az-infra, parked behind browser-OTel wiring item from dark-mode runbook §7.
2. **`auth: surface session-list + remote-revoke UI`** — owner XD + app-dev, depends on RM-004 server endpoint.
3. **`branch-protection: configure required checks on main`** — owner sec-hard (also blocks dark-mode runbook §7 item 3).

---

## 7. Six-squad sign-off ledger

| Squad | Artifact / commit | Status |
|---|---|---|
| ideation-research-planning | RM PRD (incoming) | ⏳ |
| experience-design | RM-002 patch | ⏳ |
| application-development | RM-003 + RM-004 patches | ⏳ |
| azure-infrastructure | RM-004 key-vault TTL note | ⏳ |
| security-hardening | RM-005 patch + threat-model APPROVED | ⏳ |
| quality-testing | smoke §3 + new unit tests green | ⏳ |
| review-deployment | this runbook + `remember-me-gate.yml` + squash-merge | ✅ scaffold landed |

Squash-merge blocked until rows 1–6 are ✅ and §2 gates + §3 smoke are green.
