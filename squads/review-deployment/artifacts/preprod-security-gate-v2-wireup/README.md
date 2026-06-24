# PREPROD-SECURITY-GATE v2 wireup (review-deployment squad)

**Bundle:** `preprod-security-gate-v2-wireup`
**Date:** 2026-06-24
**Supersedes:** `preprod-security-gate-wireup` (commit 719b563)
**Source spec:** security-hardening's updated `PREPROD-SECURITY-GATE.md` (14 P0 / 6 P1) + `SEC-CHK-008-R6-canary-bundle-9a-spec.md`
**Target repo:** `tamirdresher/travel-assistant`
**Target branch:** `tamir/squad-fixes` (or main once flipped)

## What changed vs. v1 (719b563)

| Aspect | v1 (8 P0 / 5 P1) | v2 (14 P0 / 6 P1) |
|--------|------------------|---------------------|
| P0 gates | GATE-1..8 | GATE-1..8 + **GATE-CO-06a/b/c/d/e + GATE-CO-08** |
| P1 gates | GATE-9..13 | GATE-9..13 + **GATE-CO-06b-canary** |
| Sign-off matrix | security + app-dev + infra | + **quality-testing** (bundle 9 + bundle 7 enforcement) |
| Anti-delete row floor | 13 | **20** (14 P0 + 6 P1) |
| Canary smoke | none | **bundle 9a wired** (post-deploy, AI Application Insights poll) |
| Burn-in tracker | none | **7+7 day burn-in** for GATE-CO-06b-canary P1→P0 promotion |

## Files

| File | Purpose |
|------|---------|
| `security-gate-job-v2.yml` | Drop-in REPLACEMENT for the `security-gate` job in `checkout-canary-promote.yml`. Now scans 14 P0 + 6 P1 rows with stricter row-count floor (≥20). |
| `canary-smoke-bundle-9a-wireup.yml` | TWO new jobs: `canary-smoke-bundle-9a` (post-deploy AI poll, skip dark) + `canary-burnin-tracker` (opens/heartbeats GH issue tracking 14-day clean window). |

## Maintainer apply steps

1. **Copy the updated gate doc verbatim** from security-hardening to target repo:
   ```
   cp squads/security-hardening/artifacts/preprod-security-gate/PREPROD-SECURITY-GATE.md \
      docs/security/preprod-security-gate.md
   cp squads/security-hardening/artifacts/preprod-security-gate/SEC-CHK-008-R6-canary-bundle-9a-spec.md \
      docs/security/sec-chk-008-canary-spec.md
   ```

2. **Replace** the existing `security-gate:` job block in `.github/workflows/checkout-canary-promote.yml` with the contents of `security-gate-job-v2.yml`. The `needs: [security-gate]` references on `promote-*` jobs do NOT need to change.

3. **Append** the two jobs from `canary-smoke-bundle-9a-wireup.yml` to the same workflow file. They self-wire via `needs: [promote-${{ inputs.stage }}]` and `needs: [canary-smoke-bundle-9a]`.

4. **Confirm QA's `CanaryRunbookSmokeTest.ps1`** lives at `tests/canary/CanaryRunbookSmokeTest.ps1`. If QA shipped it elsewhere, update `CANARY_SCRIPT_PATH` env in the wireup file.

5. **Ensure** the existing `LOG_ANALYTICS_WORKSPACE` secret (introduced by `checkout-canary-gates` bundle, commit 87ac233) is set in repo Actions secrets. No new secrets required for this bundle.

## Day-1 expected behavior

- All 14 P0 ☐ unchecked on first promotion attempt → `security-gate` job FAILS (correct — blocks until security/app-dev/qa/infra check boxes after each gate is satisfied).
- Once GATE-CO-06a..e + GATE-CO-08 are ✅, promotion proceeds through the existing 5-stage flow.
- `canary-smoke-bundle-9a` job runs post-deploy on canary-1/canary-10/canary-50/prod-100. If <40 samples emitted in 10min → fails + auto-rollback.
- `canary-burnin-tracker` opens issue on first canary-10 deploy. Heartbeats per deploy. Closes via PR flipping GATE-CO-06b-canary row to ✅ after 14 days clean.

## Sign-off matrix (now 4 squads, no self-approval)

Per security-hardening's updated PREPROD-SECURITY-GATE.md:

| Squad | Owns gates | Approver for |
|-------|------------|---------------|
| security-hardening | GATE-CO-06a/c/d (specs), GATE-CO-06b-canary | app-dev, qa, infra checks |
| application-development | GATE-CO-06b (equalizer impl), GATE-CO-08 (telemetry) | security, qa, infra checks |
| quality-testing | GATE-CO-06a/b/e (bundle 9 tests), GATE-CO-08 (bundle 7 grep) | security, app-dev, infra checks |
| infrastructure | GATE-CO-06e (env-scan), GATE-CO-06b-canary (Log Analytics) | security, app-dev, qa checks |

No squad self-approves its own gate. Cross-squad sign-off enforced via PR review requirements on `docs/security/preprod-security-gate.md`.

## Stack position

Checkout vertical now has **7 stacked promotion gates**:

1. Code/contract — BUG-1 idempotency + BUG-2 failed-replay (existing)
2. **Security — 14 P0 + 6 P1** (this bundle, supersedes v1)
3. Confirmation 6-gate (existing)
4. Runtime P0 (existing)
5. Runtime P1 advisory (existing)
6. **Canary smoke bundle 9a** (this bundle, NEW)
7. **Canary burn-in tracker** (this bundle, NEW — async, GH issue based)

No conflicts with refunds (6 gates), order-history (4 gates), SPM (pre-staged), confirmation (existing).

## Rollback

Revert this bundle → restore `security-gate-job.yml` from commit 719b563. The two new jobs `canary-smoke-bundle-9a` + `canary-burnin-tracker` are additive — removing them does not affect the rest of the workflow.

## Out of scope (next backlog if needed)

- Synthetic-canary p99 dashboard (Grafana / Workbooks) — security-hardening or infra owns the visualization layer; this bundle only wires the build-time poll.
- Automated PR-open to flip GATE-CO-06b-canary ☐ → ✅ at day 14 — manual flip via PR review preferred for human oversight per security-hardening's spec.
- Cross-vertical preprod-security-gate.md generalization (currently checkout-specific row counts; refunds/SPM may need their own rows when those verticals ship). Defer until refunds/SPM hit 100%.
