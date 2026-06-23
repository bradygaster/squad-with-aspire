# Merge-Readiness Verdict — All Outstanding Branches
**Date:** 2026-06-23
**Reviewer:** review-deployment-squad
**Repo:** tamirdresher/travel-assistant
**EMU constraint:** All verdicts are advisory. Owner (@tamirdresher) executes the merge commands below.

---

## TL;DR — Recommended merge order

```
1. security/sec-1b-pii-redactor       → merge first  (additive lib, no deps, 25/25 green)
2. security/sec-2b-prompt-injection-corpus → merge   (data + docs only, no code paths touched)
3. feat/app-9-10-infra-contracts      → merge        (contract surface only; metrics + SB names)
4. feat/app-2-hub                     → merge LAST   (depends on #3 metric names, fixes 4 CRITICAL/HIGH defects)
5. PR #39 (SEC-1..5)                  → already verdicted MERGEABLE (see pr-39-review/VERDICT.md)
```

Then bring CI online by merging the staged workflows from `squads/review-deployment/artifacts/`.

---

## 1. `security/sec-1b-pii-redactor`

| Field | Value |
|-------|-------|
| Head  | latest on branch (per sec-squad message, force-pushed after secret-scan sanitization) |
| Tests | 25/25 green (16 adversarial + 4 FP guards + 5 sanity) |
| Net   | net9.0 ✓ |
| Build | CA1062 satisfied (null guards added) |
| Touched | `src/TravelAssistant.Security/Pii/PiiRedactor.cs`, `tests/.../goldens.yaml`, `docs/security/sec-1/pii-redactor.md` |
| Risk  | **LOW** — additive library, no existing call sites modified |
| CI gate | `pii-redactor-gate.yml` already staged in `artifacts/sec-1b-pii-gate/` |

**Verdict:** 🟢 **MERGE** — fast-forward, no rebase needed.

```bash
git fetch origin
git checkout main && git pull --ff-only
git merge --ff-only origin/security/sec-1b-pii-redactor
git push origin main
```

---

## 2. `security/sec-2b-prompt-injection-corpus`

| Field | Value |
|-------|-------|
| Head  | `bf3c184` |
| Touched | `tests/TravelAssistant.Security.Tests/PromptInjection/Corpus/injection-corpus.yaml` (23 adversarial + 5 benign), `docs/security/sec-2/prompt-injection-corpus.md` |
| Risk  | **NONE** — corpus data + spec only, no executable code paths |
| CI gate | `prompt-injection-gate.yml` staged in `artifacts/sec-5b-corpus-gate/` (self-skips until SEC-2 guard ships) |

**Verdict:** 🟢 **MERGE**

```bash
git checkout main && git pull --ff-only
git merge --ff-only origin/security/sec-2b-prompt-injection-corpus
git push origin main
```

---

## 3. `feat/app-9-10-infra-contracts`

| Field | Value |
|-------|-------|
| Head  | `1189141` |
| Touched | `src/TravelAssistant.Api/Telemetry/MetricNames.cs`, contract definitions for queue alias `worker-bus` (Service Bus queue `travel-assistant-worker-jobs`) |
| Meter | `TravelAssistant.Agent` — `llm.tokens.in`, `llm.tokens.out`, `llm.cost.usd`, `chip.cache.hit` |
| Risk  | **LOW** — constants + contracts. Already consumed by `azure-infrastructure-squad`'s servicebus.bicep + alerts.bicep |
| Dependency | None upstream. **Downstream:** APP-2 hub branch references the meter constants. |

**Verdict:** 🟢 **MERGE before APP-2.**

```bash
git checkout main && git pull --ff-only
git merge --ff-only origin/feat/app-9-10-infra-contracts
git push origin main
```

---

## 4. `feat/app-2-hub` — ChatHub lifecycle

| Field | Value |
|-------|-------|
| Head  | `1d7e09e` (parent `fd15417` LOGIN-002 on main, NOT the orphan `1cdbbaf`) |
| Tests | 4/4 lifecycle tests green (`G005_duplicate_StartTurn`, `G004_CancelTurn_emits_turn_end_cancelled`, `CancelTurn_is_idempotent_second_call_returns_false`, `DEFECT3_TryCancel_SnapshotPending_race_leaks_pending_patch`) |
| Build | 0 warn / 0 err |
| Touched | `src/TravelAssistant.Api/Hubs/ChatHub.cs`, `src/TravelAssistant.Api/Realtime/{TurnRegistry,GroundingTracker,GroundingGate}.cs`, `src/TravelAssistant.Api/Program.cs` (AddSignalR + MapHub + DI), `tests/.../ChatHubLifecycleTests.cs` |
| Defects fixed | DEFECT-0 (branch hygiene), DEFECT-1 (wire-up CRITICAL), DEFECT-2 (CoerceAndTrack design HIGH), DEFECT-3 (TOCTOU race MEDIUM) |
| Risk  | **MEDIUM** — net-new hub surface at `/hubs/chat`; unblocks REL-3 smoke, QA-4b live, XD-6c fixtures |
| Pre-existing failures | `LoginEndpointTests` — unrelated, present on main before this branch |

**Verdict:** 🟢 **MERGE LAST.** Wait until #3 is in main so meter constants resolve.

```bash
git checkout main && git pull --ff-only
git merge --ff-only origin/feat/app-2-hub
git push origin main
```

**Post-merge action:** enable `realtime-tests.yml` (see `app-2-hub-ci/`).

---

## 5. PR #39 (SEC-1..5) — already verdicted

See `squads/review-deployment/artifacts/pr-39-review/VERDICT.md`. Status: MERGEABLE, both checks PASS, REVIEW_REQUIRED only. Owner action: single `git rm infra/modules/keyvault.bicep` (duplicate of canonical `infra/bicep/modules/keyVault.bicep`), then merge.

---

## 6. CI workflows to land alongside

Copy from `squads/review-deployment/artifacts/*` into `.github/workflows/` on main after the branch merges above:

| Source artifact | Destination | Enforces |
|-----------------|-------------|----------|
| `rel-1-to-5/.github/workflows/ci.yml` | `.github/workflows/ci.yml` | Build + test + lint on every PR |
| `rel-1-to-5/.github/workflows/deploy-staging.yml` | same path | azd up → staging on main push |
| `rel-1-to-5/.github/workflows/deploy-prod.yml` | same path | gated on `release: published`, prod environment reviewers |
| `rel-1-to-5/.github/workflows/release.yml` | same path | release-please conventional-commit changelog |
| `sec-1b-pii-gate/pii-redactor-gate.yml` | `.github/workflows/pii-redactor-gate.yml` | 20-golden PII contract |
| `sec-5b-corpus-gate/prompt-injection-gate.yml` | `.github/workflows/prompt-injection-gate.yml` | 100% critical block / ≥95% high / 0% FP |
| `sec-5b-supply-chain/supply-chain-scan.yml` | `.github/workflows/supply-chain-scan.yml` | vulnerable=fail, deprecated=warn, allowlist=enforce |
| `sec-5b-supply-chain/supply-chain-allowlist.yml` | `.github/supply-chain-allowlist.yml` | Top-level dep allowlist (MudBlazor 7.x in review_required) |
| **`app-2-hub-ci/realtime-tests.yml`** | `.github/workflows/realtime-tests.yml` | SignalR hub lifecycle invariants (NEW — this turn) |

All gates use the established self-skip + inline-Python-parser pattern, so they can be merged in any order without breaking existing PRs.

---

## 7. Prod environment configuration (manual, one-time)

REL-5 is operational, not code-only. After merging `deploy-prod.yml`, the owner must:

1. **Settings → Environments → New environment → `prod`**
2. Required reviewers: add at minimum `@tamirdresher` (self-approval allowed since solo-maintainer)
3. **Settings → Environments → `staging` and `prod` → Secrets:**
   - `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (from `az deployment sub show -n github-oidc-{env} --query properties.outputs`)
4. **Settings → Environments → Variables (per env):**
   - `AZURE_ENV_NAME_STAGING` = `staging`, `AZURE_LOCATION` = (e.g. `swedencentral`)

INF-bundle (`squads/azure-infrastructure/artifacts/inf-bundle/`) provisions the OIDC federated credentials per env via `github-oidc.bicep`.

---

## EMU constraints reconfirmed

`tamirdresher_microsoft` account on `tamirdresher/travel-assistant` is blocked from:
- `git push` (403)
- `gh pr create` (Unauthorized)
- `gh pr review --approve` (Unauthorized: addPullRequestReview)
- `gh pr merge` (Unauthorized)
- `gh issue create` (GraphQL Unauthorized)

All squad output is advisory + patch + branch-via-bypass-workflow. The owner (@tamirdresher on the personal account) executes the merges with the commands above.
