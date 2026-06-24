# Canary Smoke Script + Bundle 2/4 Amendments

**Session:** 2026-06-24T08:53Z
**Trigger:** (1) review-deployment Q on `CanaryRunbookSmokeTest.ps1` path; (2) app-dev `bbc2faa` amending DR-CO-005 (15min/per-cart) + 3DS shape (redirect-only).

## Files

1. **`tests/canary/CanaryRunbookSmokeTest.ps1`** — pwsh script honoring SEC-CHK-008 R6 contract from `canary-smoke-bundle-9a-wireup.yml`. Path pinned to review-deployment's default.
2. **`bundle-2-idempotency-amendment.patch.md`** — adopts `IdempotencyContract.cs` constants via `using static`; deletes magic strings for envelope codes; adds 3 assertions binding to `TtlMinutes=15` + `Scope="per-cart"`.
3. **`bundle-4-3ds-resume-happy-path.cs.patch.md`** — new test exercising redirect-out → return-with-provider-query flow (Stripe + Adyen, Theory).

## Apply Order

- Script lands today (review-deployment is wiring path now).
- Bundle 2 amendment lands when `wi-checkout-1-backend/` ships.
- Bundle 4 3DS-resume ships when `wi-checkout-1-backend/` ships.

## Script Contract (per SEC-CHK-008 R6)

- pwsh, params: `-WorkspaceId`, `-MinSamples` (default 40), `-WindowMinutes` (default 10), `-P99FloorMs` (default 1000), `-Stage`
- Queries Log Analytics via `Search-AzGraph` / `Invoke-AzOperationalInsightsQuery` for canary samples (`role_name == "checkout-api"`, custom dim `deploymentStage == $Stage`)
- Exit 0 if ≥MinSamples AND p99 ≤ P99FloorMs
- Exit 1 if <MinSamples (insufficient signal — fail closed, triggers rollback)
- Exit 2 if p99 > P99FloorMs (latency regression)
- Exit 3 if Log Analytics query fails (fail closed)

## Net deltas vs prior bundles

- **Bundle 2 (duplicate-submit-protection):** unchanged envelope assertions; adds 3 constant-binding assertions. Test count unchanged.
- **Bundle 4:** test count 3 → 4 (adds `Confirm3DSResumeHappyPath` Theory, 2 rows: stripe + adyen).
- **Security gate G7** (security-hardening's caveat): `rawProviderReason_internal` field naming + client-side grep gate is owned by security-hardening, asserted in QA bundle 11 test 2. Already absorbed last turn — no re-ship.

## Open Q to review-deployment (none)

Path pinned at `tests/canary/CanaryRunbookSmokeTest.ps1` — review-deployment's default. No `CANARY_SCRIPT_PATH` patch needed.
