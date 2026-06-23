# REL-1..REL-5 — Apply Instructions

Owner-only (EMU blocks pushes from the squad account). Run from a clean checkout of `tamirdresher/travel-assistant`.

## 1. Apply patches

```bash
cd /path/to/travel-assistant
git checkout main && git pull
git checkout -b ci/rel-1-to-5

# Patches are in this squad's artifact dir (copy them somewhere accessible first)
git am /path/to/rel-1-to-5.patch
git am /path/to/rel-4-5.patch

git push -u origin ci/rel-1-to-5
gh pr create --fill --base main --head ci/rel-1-to-5 \
  --title "ci(rel): REL-1..REL-5 CI/CD, release automation, prod gate" \
  --body "Delivers REL-1 (CI), REL-2 (PR template + CODEOWNERS), REL-3 (CD staging+smoke+rollback), REL-4 (release-please SemVer), REL-5 (prod approval gate). See docs/release-process.md."
```

## 2. Pre-merge verification (run locally before merging)

```bash
# Lint workflows
for f in .github/workflows/*.yml; do
  echo "== $f"
  python -c "import yaml,sys; yaml.safe_load(open('$f'))" || exit 1
done

# Build proves the repo isn't broken by the changes
dotnet restore TravelAssistant.slnx
dotnet build TravelAssistant.slnx -c Release --no-restore
dotnet test  TravelAssistant.slnx -c Release --no-build
```

## 3. Operational gates (NOT code — owner must do these)

| Gate | Action | Owner |
|---|---|---|
| REL-1 secrets | **Per-environment, NOT repo-level.** Settings → Environments → `staging` (and `prod`) → Secrets: add `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. Values from `az deployment sub show -n github-oidc-{env} --query properties.outputs` (see azure-infrastructure-squad's `docs/ops/oidc-setup.md`). Federated — no client secret. | tamirdresher |
| REL-3 staging env | Settings → Environments → create `staging`; no reviewers required | tamirdresher |
| REL-5 prod env | Settings → Environments → create `prod`; **add ≥1 required reviewer**. Without this the gate is a no-op | tamirdresher |
| ACR pull/push | Container Apps managed identity needs `AcrPull` on the registry; CI principal needs `AcrPush` (handled inside `azd deploy`) | azure-infrastructure-squad |
| Workflow permissions | Settings → Actions → General → Workflow permissions = "Read and write"; check "Allow GitHub Actions to create and approve pull requests" (for release-please) | tamirdresher |

## 4. Resource-name resolution (auto-discovered)

`deploy-staging.yml` and `deploy-prod.yml` resolve canonical names via `azd env get-value` from Bicep outputs published by azure-infrastructure-squad:

| Bicep output | Used for | Fallback if output missing |
|---|---|---|
| `RESOURCE_GROUP_NAME` | RG for `az containerapp` calls | `rg-travel-assistant-{env}` |
| `API_CONTAINER_APP_NAME` | Container App targeted for snapshot/rollback | `ca-api-{env}`, then `az containerapp list` discovery |
| `AZURE_CONTAINER_REGISTRY_NAME` | ACR for push (consumed by `azd deploy` internally) | n/a — azd uses its own discovery |

Confirmed canonical names from `infra/bicep/modules/*`:
- Container App: `ca-api-{env}` (staging|prod)
- ACR: `acrtravel{short}` where `short ∈ {dev, stg, prod}` — no dashes (ACR naming rules)
- RG: `rg-travel-assistant-{env}`

No edits required if Bicep outputs are present; the workflows fall through cleanly otherwise.

## 5. Cross-squad handoffs (non-blocking, but smoke/version will fail until done)

- **application-development-squad** — implement APP-8 `/api/version` endpoint returning `{version, commit, buildTime}` from `version.txt` + `GITHUB_SHA` build arg. Referenced by `docs/release-process.md` and `tests/smoke`.
- **quality-testing-squad** — create `tests/smoke/` Playwright project hitting `STAGING_BASE_URL` (`/healthz`, `/api/version`, chat page + SignalR negotiate). `deploy-staging.yml` detects and skips cleanly if missing, so this doesn't block merge — but REL-3 smoke acceptance requires it.

## 6. Environment scope decision (confirmed with azure-infrastructure-squad)

**Lower env is `staging`, not `dev`.** Local AppHost provides per-dev full-stack via emulators; a 4th persistent Container Apps env was cut to avoid doubling spend. `main` → `staging` → tag/release → `prod`. INF-6 (multi-env / PR previews) stays P2 unless app-dev squad files a need. Planning's original "main → dev" REL-3 acceptance language is superseded — read it as "main → staging".

## 7. First release (after merge)

release-please opens a release PR after the first conventional commit lands on main. Merging that PR creates tag `v0.1.0` → `release.published` → `deploy-prod.yml` queues, prod env reviewer approves, prod deploy runs.

## 8. PR triage (separate workstream, no patches needed)

| PR | Action |
|---|---|
| #26, #28, #33, #38 | Mergeable — owner squash-merge |
| #39 (SEC-1..5) | Apply gitleaks allowlist patch (see prior session notes), then merge |
| #29, #31 | Hold — request rebase from author |
| #30 | Conflicting — request rebase |
| #32 | Out of v0.1 scope — close with comment |

Owner action required (EMU blocks `gh pr merge` and `gh pr review --approve` from the squad account).
