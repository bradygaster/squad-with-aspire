# QA Runtime Gate Wire-Up (Gate 12 + Gate 13)

Companion to security-hardening's `preprod-security-gate-wireup` (checkbox grep) and QA's `PreprodSecurityGateTests.cs` (runtime assertions).

## What this adds

| Gate | Source | Blocking? | What it asserts |
|------|--------|-----------|-----------------|
| 10   | security-hardening `security-gate` job | Yes — needs on every promote-* | Doc-level: rows present, freshness, sign-off matrix |
| 11   | confirmation `synthetic-order-status.sh` | Yes (existing) | Order-status canary probes (5 states, IDOR-404, cache) |
| **12** | **QA `runtime-gate-p0` job (this bundle)** | **Yes — needs on every promote-***  | **Runtime: 8 P0 `[Fact]`s against dark revision** |
| **13** | **QA `runtime-gate-p1` job (this bundle)** | **No — advisory, opens SLA issue** | **Runtime: 5 P1 `[Fact]`s, 7-day SLA tracking** |

## Maintainer apply (EMU-blocked from PR, hand-apply path)

1. **Drop QA test file** at `tests/TravelAssistant.Api.Tests/Security/PreprodSecurityGateTests.cs` (sourced from QA's bundle: `squads/quality-testing/.squad/session-state/945b3a7a-3dcc-453e-af3a-50be7356cdbe/files/preprod-gate-verification/`).
2. **Append both jobs** from `runtime-gate-jobs.yml` into `.github/workflows/checkout-canary-promote.yml` (after the `security-gate` job, before the `promote-*` jobs).
3. **Apply `workflow-patch.diff`** to add `runtime-gate-p0` to the `needs:` list of every `promote-*` job. P1 stays decoupled.
4. **Verify `deploy-dark` job outputs** `revision_fqdn` and `revision_name`. If not, add them — the gate jobs read both.
5. **Create GH Environment** `preprod-canary` with the 4 required reviewers from the security-hardening sign-off matrix (so a manual approval is forced before tests even attempt to hit the dark revision).

## Why P0 reruns at every stage (not just dark)

A regression caught at 10% must halt the 50% promotion. Wiring `runtime-gate-p0` as `needs:` on every `promote-*` makes the gate re-evaluate at each stage boundary. Same pattern as security-hardening's checkbox gate.

Trade-off: ~2 min of redundant test execution per stage. Worth it — beats the alternative (a P0 silently regressing between 10% and 50% because the gate only ran once at dark).

## Rollback wiring

`runtime-gate-p0` failure invokes the existing `./.github/actions/checkout-rollback` composite action (App Config kill-switch flip + traffic shift to stable + revision deactivate). No new rollback code — reuses what's already in the canary-gates bundle.

## Secrets / Environments required

Already provisioned by prior bundles:
- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (OIDC)
- `APP_CONFIG` (kill-switch endpoint)
- `GITHUB_TOKEN` (default, for issue creation on P1 failure)

New environment to create:
- `preprod-canary` — manual approval required, 4 reviewers (app-dev / qa / security / infra leads, matching the sign-off matrix).

## Day-1 expected behavior

- **GATE-09 (WAF) skipped** in canary env if no Front Door / App Gateway in front. QA's test marks it `Skip="canary env has no WAF"` — verify before first run, else expect 1 P0 failure.
- **GATE-04, GATE-11** will fail loudly if app-dev hasn't shipped refresh-token revocation + T13 caps. That's correct — gate is doing its job.
- **GATE-13 (JCS shim LOC)** will fail until security swaps the shim for a vetted lib. Acceptable on day-1; tracks via P1 SLA issue.

## Out of scope (next backlog if needed)

- Coverage for confirmation/order-status flows (QA owns adding `Tier=P0` tests for WI-CONFIRM-1..3 if they want runtime gating).
- Cosmos RU pressure assertions (separate perf test, not a gate test).
- Multi-region failover gate (post-go-live concern).

## Apply order in the stack

```
infra Bicep #44 (3 must-fix amendments)
  → QA test bundle (un-skip)
  → checkout-cicd.yml
  → checkout-canary-promote.yml
  → confirmation-canary-gate bundle (additive Gate 11)
  → preprod-security-gate-wireup (Gate 10)
  → THIS BUNDLE (Gate 12 + 13)
```

After this lands, the checkout vertical promotion workflow has **5 stacked gates**:
- Code/contract (BUG-1 + BUG-2)
- Confirmation 6-gate (Gate 11)
- Security doc + sign-off (Gate 10)
- Runtime P0 (Gate 12)
- Runtime P1 advisory (Gate 13)

— review-deployment-squad

> "Markdown can lie. Tests can't."
