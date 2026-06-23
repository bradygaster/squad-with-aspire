# PR #39 Review Verdict — review-deployment-squad

**Branch:** `security/sec-1-to-5-hardening` → `main`
**Status:** ✅ **APPROVED (advisory)** — owner click required (EMU blocks squad approve)
**Date:** 2026-06-23

## State at review

| Check | Result |
|---|---|
| `mergeable` | MERGEABLE |
| `mergeStateStatus` | BLOCKED (REVIEW_REQUIRED only — no conflicts, no failing checks) |
| `secret-scan / gitleaks` | ✅ SUCCESS |
| `secret-scan / appsettings-no-secrets` | ✅ SUCCESS |
| Files changed | 17 added, 0 modified, 0 deleted |

## Scope verified

SEC-1..5 in one PR, matching the planning brief:

- **SEC-1** Secrets policy + `.gitleaks.toml` + `secret-scan.yml` ✓
- **SEC-2** Prompt-injection sanitizer + tool-call guard + 6 tests ✓
- **SEC-3** SSRF-guarding `HttpMessageHandler` + URL allowlist YAML + doc ✓
- **SEC-4** PII taxonomy + privacy doc ✓
- **SEC-5** `ProductionGuard` + 5 tests + Aspire prod-hardening doc ✓

Plus `infra/modules/keyvault.bicep` (RBAC, soft-delete, purge-protection). See cleanup item below.

## Gitleaks allowlist review

Allowlist additions in `2133267` are scoped tight enough to merge:

**Paths** (5 entries):
- `tests/.*Fixtures.*` — narrow, fixture-only
- `tests/.*\.Tests/Security/.*` — narrow, security test fixtures only
- `docs/security/.*\.md` — narrow, doc examples
- `docs/security/.*\.yaml` — narrow, URL allowlist examples
- `infra/.*\.bicep` — **broader than ideal** but acceptable. Bicep should never embed real secrets (they go through KV refs), so the false-positive surface is well-known Azure constants (RBAC GUIDs, resource provider IDs). Tightening this requires per-file allowlist which gitleaks doesn't model cleanly. **Accept as-is, revisit in SEC-5b.**

**Regexes** (5 entries):
- `AKIAIOSFODNN7EXAMPLE` — AWS public example key ✓
- `00000000-0000-0000-0000-000000000000` — null GUID ✓
- `AccountKey=abc` — Cosmos emulator literal, used in `ProductionGuardTests.cs` to PROVE the guard rejects emulators ✓
- `4633458b-…`, `b86a8fe4-…`, `00482a5a-…` — Azure built-in RBAC role definition GUIDs (public, documented at learn.microsoft.com/azure/role-based-access-control/built-in-roles) ✓

No real secret-shaped strings allowlisted. Risk acceptable.

## Owner actions

```bash
# 1. Approve + merge PR #39
gh pr review 39 --repo tamirdresher/travel-assistant --approve \
  --body "Reviewed per review-deployment-squad VERDICT (squads/review-deployment/artifacts/pr-39-review/VERDICT.md). Gitleaks allowlist scoped narrow; bicep glob accepted with SEC-5b follow-up."
gh pr merge 39 --repo tamirdresher/travel-assistant --squash --delete-branch

# 2. Cleanup: delete redundant top-level KV bicep (post-merge)
git checkout main && git pull
git checkout -b chore/remove-redundant-keyvault-bicep
git rm infra/modules/keyvault.bicep
git commit -m "chore(infra): remove redundant top-level keyvault.bicep

Canonical KV is infra/bicep/modules/keyVault.bicep (azure-infra owns).
Top-level draft from PR #39 superseded.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
git push -u origin chore/remove-redundant-keyvault-bicep
gh pr create --fill --repo tamirdresher/travel-assistant
```

## Follow-ups (already filed, not blockers)

- **SEC-1b** — Redactor implementation (security-hardening-squad owns)
- **SEC-2b** — Prompt-injection corpus to ≥20 payloads (security-hardening-squad owns)
- **SEC-5b** — Dep + container scan CI; revisit `infra/**.bicep` allowlist scope at the same time (security-hardening-squad owns)
- **Cleanup** — Delete top-level `infra/modules/keyvault.bicep`; canonical KV is `infra/bicep/modules/keyVault.bicep` (azure-infrastructure-squad owns). Patch attached.

## Why this verdict is advisory, not a click

`gh pr review --approve` returns `Unauthorized: addPullRequestReview` from `tamirdresher_microsoft` (EMU constraint). Same blocker as `ci/rel-1-to-5` apply, same blocker as the 8 untouched open PRs. This VERDICT.md is the audit-trail substitute. Owner click is the final approval.
