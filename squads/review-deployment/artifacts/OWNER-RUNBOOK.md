# Owner Runbook — Final Merge & CI Activation Sequence

**Date:** 2026-06-23
**Owner:** @tamirdresher (only account with write access — EMU walls every other squad account)
**Repo:** `tamirdresher/travel-assistant`
**Status:** Code complete, tests green, all artifacts staged. This doc is the single source of truth for landing everything.

---

## 0. EMU Constraint Recap

`tamirdresher_microsoft` (the squad service account) is **blocked from**: `git push`, `gh pr create`, `gh pr review --approve`, `gh pr merge`, `gh issue create`, and `gh api PUT contents`. Every artifact produced this cycle is a patch or staged file the owner must apply manually. **Do not block on the squads for any merge action — they cannot execute it.**

---

## 1. Branch Merge Order (Strict)

Each step assumes the prior is merged to `main` and CI is green. Run from a clean working tree on `main`.

### Step 1 — `security/sec-1b-pii-redactor`
**Why first:** Additive library only (`src/TravelAssistant.Security/Pii/PiiRedactor.cs`), zero callers in main yet, 25/25 tests green. Cannot break anything downstream.
```bash
gh pr list --head security/sec-1b-pii-redactor --json number -q '.[0].number'   # → $PR
gh pr merge $PR --squash --delete-branch
```

### Step 2 — `security/sec-2b-prompt-injection-corpus`
**Why second:** YAML corpus + docs only. No code paths. Companion CI gate (`prompt-injection-gate.yml`) self-skips when guard project absent, so safe to land before SEC-2 guard implementation.
```bash
gh pr list --head security/sec-2b-prompt-injection-corpus --json number -q '.[0].number'   # → $PR
gh pr merge $PR --squash --delete-branch
```

### Step 3 — `feat/app-9-10-infra-contracts`
**Why third:** Defines OTel meter `TravelAssistant.Agent` + metric constants (`src/TravelAssistant.Api/Telemetry/MetricNames.cs`) + Service Bus queue name `travel-assistant-worker-jobs`. APP-2 hub (Step 4) depends on these constant names.
```bash
gh pr list --head feat/app-9-10-infra-contracts --json number -q '.[0].number'   # → $PR
gh pr merge $PR --squash --delete-branch
```

### Step 4 — `feat/app-2-hub`
**Why fourth:** SignalR hub fixes 4 defects (DEFECT-0..3). Tip `1d7e09e` parent `fd15417` (clean rebase, **not** the orphan `1cdbbaf` from the original branch). Lifecycle tests 4/4 green. Depends on Step 3's metric names.
```bash
# Verify branch tip first
git fetch origin feat/app-2-hub
git log --oneline -1 origin/feat/app-2-hub   # MUST show 1d7e09e (or descendant)

gh pr list --head feat/app-2-hub --json number -q '.[0].number'   # → $PR
gh pr merge $PR --squash --delete-branch
```

### Step 5 — PR #39 (SEC-1..5 Key Vault + secrets policy)
**Why fifth:** Independent of Steps 1-4 but waited on `pr-39-review/VERDICT.md` verdict (MERGEABLE, both checks pass, REVIEW_REQUIRED only). Owner is sole approver under EMU.
```bash
gh pr review 39 --approve
gh pr merge 39 --squash --delete-branch
```

### Step 6 — PR #43 (chore: delete redundant `infra/modules/keyvault.bicep`)
**Why sixth:** Must follow PR #39 because #39 establishes the canonical KV path at `infra/bicep/modules/keyVault.bicep`. Branch `security/kv-cleanup-redundant-module @ 351a344`. Pure deletion (102 LOC removed) + 2 doc ref updates. Zero runtime refs verified.
```bash
gh pr review 43 --approve
gh pr merge 43 --squash --delete-branch
```

### Steps 7-10 — Remaining triaged PRs (from prior verdict)
| PR | Disposition | Command |
|----|------------|---------|
| #26, #28, #33, #38 | Ready to merge | `gh pr merge <N> --squash --delete-branch` |
| #29, #31 | Rebase needed first | Comment on PR asking author to rebase; do not merge until conflicts resolved |
| #30 | Conflicting | Close or request rebase |
| #32 | Out of scope | `gh pr close 32 --comment "Closing — out of scope for v0.1"` |

---

## 2. CI Workflow Activation (after Step 6)

All workflows are staged under `squads/review-deployment/artifacts/`. Each is self-skipping when its target project/corpus is absent, so they are **safe to merge as a single PR even if upstream features are not yet wired**.

### Single combined PR (recommended)

```bash
git checkout main && git pull
git checkout -b ci/activate-all-gates

mkdir -p .github/workflows .github

# REL-1..6 — base CI + deploy
git apply squads/review-deployment/artifacts/rel-1-to-5/rel-1-to-5.patch
git apply squads/review-deployment/artifacts/rel-1-to-5/rel-4-5.patch
git apply squads/review-deployment/artifacts/rel-1-to-5/rel-6-naming.patch

# SEC gate trio
cp squads/review-deployment/artifacts/sec-1b-pii-gate/pii-redactor-gate.yml          .github/workflows/
cp squads/review-deployment/artifacts/sec-5b-corpus-gate/prompt-injection-gate.yml   .github/workflows/
cp squads/review-deployment/artifacts/sec-5b-supply-chain/supply-chain-scan.yml      .github/workflows/
cp squads/review-deployment/artifacts/sec-5b-supply-chain/supply-chain-allowlist.yml .github/

# APP-2 realtime gate
cp squads/review-deployment/artifacts/app-2-hub-ci/realtime-tests.yml .github/workflows/

git add .github docs version.txt release-please-config.json .release-please-manifest.json
git commit -m "ci: activate full CI/CD gate suite (REL-1..6, SEC-1b/2b/5b, APP-2 realtime)"
git push -u origin ci/activate-all-gates
gh pr create --fill --base main
```

### One-time owner-side prerequisites

Before the first deploy runs green:

1. **Provision OIDC federated credentials** (azure-infrastructure-squad delivered the Bicep):
   ```bash
   az deployment sub create \
     --template-file infra/bicep/modules/github-oidc.bicep \
     --location swedencentral \
     --parameters env=staging githubRepo=tamirdresher/travel-assistant
   az deployment sub show -n github-oidc-staging --query properties.outputs
   # → repeat for env=prod
   ```

2. **Configure GitHub Environments** at `Settings → Environments`:
   - Create `staging` and `prod` environments.
   - On `prod`: add **Required reviewers** = @tamirdresher (this is the REL-5 prod gate — it is **operational, not code-only**).
   - Add env-scoped secrets to each: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (values from step 1 outputs). **Do not use repo-level secrets.**
   - Add env vars: `AZURE_ENV_NAME_STAGING=staging`, `AZURE_LOCATION=swedencentral`, `AZURE_RESOURCE_GROUP=rg-travel-assistant-staging` (and equivalent for prod).

3. **Verify supply-chain allowlist** matches actual top-level deps after first run. `supply-chain-allowlist.yml` is seeded but will fail noisily on the first PR if anything is missing — add the package under `packages:` (auto-approved) or `review_required:` (sec-squad gate).

---

## 3. Post-Merge Verification

After the CI activation PR merges, push a no-op commit to verify all gates fire:

```bash
git commit --allow-empty -m "chore: verify CI gates"
git push
```

Expect on the resulting Actions run:
- ✅ `ci` (build + test, REL-1)
- ✅ `deploy-staging` (auto-runs on main; provisions via azd)
- ✅ `pii-redactor-gate` (runs against TravelAssistant.Security.Tests)
- ⏭️ `prompt-injection-gate` (skips — no SEC-2 guard tests yet, by design)
- ✅ `supply-chain-scan` (3 jobs: vulnerable, deprecated, allowlist)
- ✅ `realtime-tests` (filters on `FullyQualifiedName~Realtime|FullyQualifiedName~ChatHub`)

Any unexpected failure → check the workflow's README in `squads/review-deployment/artifacts/<gate>/` for the contract.

---

## 4. Manual Steps That Squads Cannot Automate

| Action | Owner | Why |
|--------|-------|-----|
| `git push` for any squad branch | @tamirdresher | EMU walls service account pushes |
| `gh pr review --approve` | @tamirdresher | EMU walls `addPullRequestReview` mutation |
| `gh pr merge` | @tamirdresher | Follows from approve being blocked |
| Provisioning Azure OIDC federation | @tamirdresher | Subscription-scoped `Microsoft.Authorization/roleAssignments/write` |
| Configuring GitHub Environments + required reviewers | @tamirdresher | Repo admin only |
| Promoting allowlist entries from `review_required:` → `packages:` | @tamirdresher (or delegated to sec-squad via separate PR per dep) | Policy decision |

---

## 5. Artifact Index

All paths relative to repo root.

| Artifact | Path | Purpose |
|----------|------|---------|
| Final merge verdict | `squads/review-deployment/artifacts/merge-readiness-final/VERDICT.md` | Per-branch verdict matrix |
| PR #39 verdict | `squads/review-deployment/artifacts/pr-39-review/VERDICT.md` | SEC-1..5 KV verdict |
| REL-1..6 patches | `squads/review-deployment/artifacts/rel-1-to-5/*.patch` | Base CI + deploy + release-please |
| PII redactor gate | `squads/review-deployment/artifacts/sec-1b-pii-gate/` | SEC-1b CI gate |
| Prompt injection gate | `squads/review-deployment/artifacts/sec-5b-corpus-gate/` | SEC-2b CI gate |
| Supply chain gate | `squads/review-deployment/artifacts/sec-5b-supply-chain/` | SEC-5b CI gate + allowlist |
| Realtime tests gate | `squads/review-deployment/artifacts/app-2-hub-ci/realtime-tests.yml` | APP-2 hub CI gate |
| OIDC Bicep | `squads/review-deployment/artifacts/rel-1-to-5/oidc/github-oidc.bicep` (or canonical `infra/bicep/modules/github-oidc.bicep` after azure-squad merge) | Federated creds provisioning |

---

## 6. If You Only Have 5 Minutes

```bash
# Merge the 4 branches + 2 PRs in order, then ship the CI PR.
for b in security/sec-1b-pii-redactor \
         security/sec-2b-prompt-injection-corpus \
         feat/app-9-10-infra-contracts \
         feat/app-2-hub; do
  PR=$(gh pr list --head "$b" --json number -q '.[0].number')
  [ -n "$PR" ] && gh pr merge "$PR" --squash --delete-branch
done
gh pr review 39 --approve && gh pr merge 39 --squash --delete-branch
gh pr review 43 --approve && gh pr merge 43 --squash --delete-branch
# Then follow §2 to ship the CI activation PR.
```

---

**End of runbook.** This is the final review-deployment-squad artifact for this cycle. No further squad action is possible until the owner executes the steps above.
