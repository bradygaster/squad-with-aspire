# Refunds Post-Deploy Smoke — Workflow Wiring

Wires QA's 5-test post-deploy contract smoke into `refunds-canary-promote.yml`
as a post-flip gate per stage. Pairs with the existing `synthetic-refund.sh`
probe (reachability/gate-shape) — this bundle adds contract-shape.

## Source bundles

| Layer | Source | Owner |
|---|---|---|
| Smoke spec (`refunds-post-deploy-smoke.spec.ts`) | `squads/quality-testing/.squad/session-state/d04adaf9-574b-43db-bdfb-af48311298f9/files/refunds-post-deploy-smoke/` | quality-testing (Hockney) |
| Workflow wiring (`workflow-snippet.yml`) | this folder | review-deployment |
| Canary workflow it patches (`refunds-canary-promote.yml`) | `squads/review-deployment/artifacts/refunds-canary-deploy-gate/` (commit `2a74ab9`) | review-deployment |
| Rollback composite (`checkout-rollback`) | `squads/review-deployment/artifacts/checkout-canary-gates/` (commit `87ac233`) | review-deployment |

## What this gates

| Smoke | Asserts | Failure means |
|---|---|---|
| SMOKE-1 | 4-value reason enum on 409 (DR-REFUNDS-001) | Spec drift — server returned enum value outside the frozen set |
| SMOKE-2 | No `re_*`/`pi_*`/`ch_*`/`pm_*`/`tok_*`/`src_*` in any 2xx body (SEC-RFD-001) | PCI exposure on the wire |
| SMOKE-3 | Redis idempotency replay byte-for-byte | Idempotency store regression; double-refund risk |
| SMOKE-4 | T13 `RateLimit-*` headers exposed | Client back-off contract broken |
| SMOKE-5 | Anon request → 401 | WAF + auth chain misconfigured for refunds path |

## Why post-flip and not pre-flip

The frontend-gate (`89205fa`) and security-gate already block pre-flip on
the artifact-shape. These 5 smokes need a **live canary revision**
serving traffic, because they validate request/response shape end-to-end
through ACA → API → Cosmos → Redis → Stripe sandbox. Running them
synthetically against a deploy slot is the cheapest place to catch
contract drift before real users hit it.

## Apply order (maintainer, after EMU lift or via squad-fixes branch)

1. **WI-REFUND-1** (server endpoints) merges to `main`
2. **WI-REFUND-6** (Redis idempotency + webhook dedup) merges
3. Copy QA bundle from
   `squads/quality-testing/.squad/session-state/d04adaf9-574b-43db-bdfb-af48311298f9/files/refunds-post-deploy-smoke/refunds-post-deploy-smoke.spec.ts`
   to `apps/web/tests/e2e/refunds/post-deploy/refunds-post-deploy-smoke.spec.ts`
4. Paste the 3 jobs from `workflow-snippet.yml` into
   `.github/workflows/refunds-canary-promote.yml`
5. Patch the 3 existing `runtime-gate-p0-{stage}` jobs to add the new
   `needs:` entry (diff shown at bottom of `workflow-snippet.yml`)
6. Provision the new secrets and vars below
7. First canary dispatch — verify SMOKE-1..5 all green at dark stage
   (dark skips this gate by design; first real run is canary-1)

## New repo secrets/vars

| Name | Type | Notes |
|---|---|---|
| `vars.REFUNDS_CANARY_BASE_URL` | var | ACA canary FQDN (existing per refunds-deploy-gate) |
| `vars.REFUNDS_PROD_BASE_URL` | var | ACA prod FQDN (existing) |
| `SEEDED_ORDER_REFUND_ELIGIBLE` | secret | Order id in confirmed+within-window state, canary env |
| `SEEDED_ORDER_REFUND_ELIGIBLE_PROD` | secret | Same, prod env (separate to avoid cross-env writes) |
| `SEEDED_ORDER_REFUND_WINDOW_EXPIRED` | secret | Already required by refunds-deploy-gate (`2a74ab9`) — reuse |
| `SEEDED_ORDER_REFUND_WINDOW_EXPIRED_PROD` | secret | New for prod env |
| `SYNTHETIC_REFUND_TOKEN` | secret | Short-lived JWT, `refund:write`, `sub=synthetic-canary-probe`; canary env |
| `SYNTHETIC_REFUND_TOKEN_PROD` | secret | Same, prod env |

The `sub=synthetic-canary-probe` claim is load-bearing: existing prod
SLO + billing dashboards already filter on `sub NOT IN ('synthetic-*')`,
so these smokes don't pollute KPIs or get refund-fee-billed.

## Seed-data lifecycle (handoff to azure-infrastructure)

The 2 seeded orders need to be recreated/reset per canary cycle so
SMOKE-3 (idempotency replay) has a clean state. Two options:

- **A (simple, today):** Manual reset script run before each `workflow_dispatch`. Operator owns.
- **B (later, post-WI-CANCEL-1):** Cosmos pre-deploy step in the workflow itself, seeds via management API. Requires synthetic test Cosmos write scope.

Today: ship Option A. Open a backlog issue for Option B post-cancellation v1.

## Failure → rollback

Same path as `runtime-gate-p0-*`: invokes `./.github/actions/checkout-rollback`
composite with `stage` + `reason` + `revision-prefix=refunds-api`. The
composite (a) flips App Config `refunds_v1_enabled=false` (b) shifts ACA
traffic back to last stable revision (c) deactivates the failed revision
(d) opens an incident issue tagged `incident:refunds`,`stage:{stage}`.

## What this does NOT do

- Does not replace synthetic-refund.sh (still runs as Gate 5, reachability layer)
- Does not run on dark stage (no live traffic; runtime-gate-p0 covers infra shape)
- Does not run on every commit — only on `workflow_dispatch` canary promotion
- Does not assert UI behavior — that's the frontend-gate (`89205fa`) job, which is pre-flip

## Stacked gate inventory for refunds vertical (post-this-bundle)

| # | Gate | Stage scope | Bundle |
|---|---|---|---|
| 1 | `security-gate` (8 P0 binding) | All stages | `preprod-security-gate-wireup` |
| 2 | `refunds-frontend-gate` (token grep + 15 PW + 4 axe + SR label) | Pre-flip, once | `refunds-frontend-gate` (`89205fa`) |
| 3 | `runtime-gate-p0` (QA Tier=P0 filter) | Each stage boundary | `qa-runtime-gate-wireup` |
| 4 | `runtime-gate-p1` (advisory, auto-issue) | Each stage boundary | `qa-runtime-gate-wireup` |
| 5 | `synthetic-refund.sh` (11 reachability probes) | Each stage post-flip | `refunds-canary-deploy-gate` (`2a74ab9`) |
| 6 | `post-deploy-smoke-{stage}` (5 contract specs) | canary-1, canary-10, prod-100 | **this bundle** |

Refunds vertical promotion now has 6 stacked gates. Backlog for review-deployment squad: empty.
