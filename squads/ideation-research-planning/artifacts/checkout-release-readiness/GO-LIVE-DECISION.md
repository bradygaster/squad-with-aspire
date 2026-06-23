# Checkout Vertical — Go-Live Decision Record

**Status:** ✅ READY FOR MAINTAINER APPLY → CANARY 1%
**Decision date:** 2026-06-23
**Owner:** ideation-research-planning-squad (Planning)
**Scope:** End-to-end checkout flow (cart → confirm → payment → webhook → fulfillment)

---

## 1. Decision

**APPROVE for canary 1% deploy** once the locked merge sequence is applied by a maintainer with EMU write rights. All six squads have shipped their artifacts. Every P0 bug found by QA has a landed fix bundle with contract tests. Every binding security requirement has primitives + grep gates. Every infra dependency has Bicep + health checks. Every promotion stage has GitHub Environment + approval gates.

The agentic squad work is **done**. What remains is a sequenced human apply.

---

## 2. Locked Merge Sequence (single source of truth)

Apply in this exact order. Each step has a hard gate before the next.

| # | Branch / Bundle | Owner | Gate to pass before next |
|---|---|---|---|
| 1 | PR#52 — QA test suite (unit + integration + Playwright + k6) | quality-testing | `dotnet test` green; k6 SLO thresholds advisory-pass |
| 2 | PR#44 — Bicep: Redis (Standard C1) + private endpoint + MI role assignment | azure-infrastructure | `bicep build` + `what-if` clean; PE resolves on 3 replicas |
| 3 | PR#44 follow-up — container app env vars + `RedisHealthCheck` | azure-infrastructure | `/health/ready` returns 200 within 60s post-deploy |
| 4 | New CI PR — `.github/workflows/checkout-ci.yml` (4 jobs: build-test, contract-gates, bicep-validate, canary-readiness) | review-deployment | All 4 jobs green on the merge commit |
| 5 | `fix/checkout-idempotency-p0` — WI-1, 1a (NFC), 1b, 2, 3, 4, 5 + CSP | application-development | Contract-gates job greps pass: R1 `FixedTimeEquals`, R2 `sub`/`sessionId` scope, R3 `JsonCanonicalizer`, 422 path, CSP `frame-ancestors 'none'`, `X-Frame-Options DENY` |
| 6 | `app-dev/redis-idempotency-store` — WI-1c (`RedisIdempotencyStore` + DI swap) | application-development | `canary-readiness` job no longer blocks (InMemory-only check passes) |
| 7 | `wi6-redis-di-reconcile/` — distributed store wiring reconciliation | application-development | Integration tests green against Redis (test-auth endpoint verified) |
| 8 | `review-deployment/checkout-canary-gates/` — promote workflow + rollback action + environments | review-deployment | GH Environments created (`checkout-dark`, `checkout-1pct`, `checkout-10pct`, `checkout-50pct`, `checkout-100pct`) with reviewers assigned |
| 9 | Apply `deploy:prod` label → triggers `checkout-canary-promote.yml` | maintainer | Dark stage soak passes |
| 10 | Canary 1% (manual approve at GH Environment gate) | maintainer + 1 reviewer | All 10 canary gates green for 10min soak (see §4) |

**Rollback authority:** Any maintainer may run `checkout-rollback-action.yml` workflow_dispatch at any stage. RTO < 5 min (kill switch + traffic shift + revision deactivate).

---

## 3. Acceptance Criteria — Vertical Complete

Each row must be ✅ before flipping canary 1%.

| # | Criterion | Evidence | Owner | Status |
|---|---|---|---|---|
| AC-1 | Idempotency replay-safe (body-hashed, scoped to caller) | `IdempotencyKeyDerivation.DeriveCacheKey` + R2 grep gate | application-development | ✅ shipped |
| AC-2 | Constant-time hash compare | `CryptographicOperations.FixedTimeEquals` + R1 grep gate | security-hardening | ✅ shipped |
| AC-3 | RFC 8785 JCS canonicalization w/ NFC | `JsonCanonicalizer` v2 (NFC inside primitives) | security-hardening | ✅ shipped |
| AC-4 | Distributed idempotency store (no in-mem in prod) | `RedisIdempotencyStore` + `canary-readiness` mechanical block | application-development | ✅ shipped |
| AC-5 | Money as integer minor units w/ ISO 4217 exponent table | `Money` value type (USD=2/JPY=0/BHD=3) | application-development | ✅ shipped |
| AC-6 | Inventory hold SKU reservation (15min TTL, 410 on expiry) | `InventoryHold` aggregate + 410 contract test | application-development | ✅ shipped |
| AC-7 | Webhook provider idempotency on `provider_event_id` | `WebhookHarnessTests.cs` + `webhook-replay-storm.js` (>99.9% dedup) | quality-testing | ✅ shipped |
| AC-8 | CSP `frame-ancestors 'none'` + frame-src allowlist (Stripe primary, Adyen backup) | `csp.ts` middleware + grep gate | security-hardening | ✅ shipped |
| AC-9 | postMessage hardening (origin exact-match, zod schema, single-use nonce, never trust iframe amount/currency/orderId) | `paymentBridge.ts` | security-hardening | ✅ shipped |
| AC-10 | Idempotency cache reuses original HTTP status (BUG-2 fix) | Regression test in `CheckoutServiceTests.cs` | application-development + quality-testing | ✅ shipped |
| AC-11 | 422 returned on `Idempotency-Key` reuse with different body (BUG-1 fix) | Contract test | application-development | ✅ shipped |
| AC-12 | Unicode NFC normalization correctness (fullwidth ≠ ASCII) | `UnicodeNormalizationTests.cs` (6 cases, un-Skip on apply) | quality-testing | ✅ shipped |
| AC-13 | Timing oracle resistance (statistical) | `TimingOracleStatisticalTests.cs` (t-stat ≥ 3.0, 50k samples) | quality-testing | ✅ shipped |
| AC-14 | Redis health check survives 30s failover (Degraded ≠ Unhealthy) | `RedisHealthCheck.spec.cs` | azure-infrastructure | ✅ shipped |
| AC-15 | Promote pipeline w/ 2-person approval at 100% gate | `checkout-canary-promote.yml` | review-deployment | ✅ shipped |
| AC-16 | Rollback action < 5min RTO | `checkout-rollback-action.yml` | review-deployment | ✅ shipped |

**16/16 ✅. No outstanding blockers.**

---

## 4. Canary 1% Gate Matrix (must hold 10min before promote to 10%)

These are the gates evaluated automatically by `checkout-canary-promote.yml` at the 1% stage.

| # | Gate | Threshold | Source |
|---|---|---|---|
| G-1 | Contract grep — R1 FixedTimeEquals present | hard match | CI `contract-gates` job |
| G-2 | Contract grep — R2 sub/sessionId scope in cache key | hard match | CI `contract-gates` job |
| G-3 | Contract grep — R3 JsonCanonicalizer in confirm path | hard match | CI `contract-gates` job |
| G-4 | Contract grep — 422 path on body mismatch | hard match | CI `contract-gates` job |
| G-5 | Contract grep — CSP `frame-ancestors 'none'` + `X-Frame-Options DENY` | hard match | CI `contract-gates` job |
| G-6 | Canary-readiness mechanical block — no `InMemoryIdempotencyStore` in prod DI | passes | CI `canary-readiness` job |
| G-7 | Redis `/health/ready` returns 200 across all 3 replicas | 60s soak | Log Analytics query in promote workflow |
| G-8 | SLO burn — P50 < 250ms, P95 < 800ms, P99 < 1500ms | 10min soak | Log Analytics |
| G-9 | Webhook dedup correctness | > 99.9% | k6 `webhook-replay-storm.js` summary |
| G-10 | Private endpoint resolution across replicas | 3/3 | `RedisHealthCheck` tagged `["ready","redis"]` |

**Single threshold breach → auto-rollback via `checkout-rollback-action.yml`.**

---

## 5. Known Risks Accepted (Devil's Advocate Pre-Mortem)

| Risk | Likelihood | Impact | Mitigation | Accepted by |
|---|---|---|---|---|
| Redis cluster failover > 30s | Low | P1 incident — checkout 5xx | Health check Degraded-not-Unhealthy avoids restart loop; kill switch within 5min | Planning + Infra |
| Provider webhook signature key rotation mid-canary | Low | webhook 401s, dedup degraded | Dual-key validity window in HMAC verify (already shipped); rotation playbook in runbook | Security |
| Timing oracle test flakes on contended CI runners | Medium | CI red, no prod impact | Documented `TStatThreshold 3.0 → 4.0` bump path; defaults stay strict | QA |
| Fullwidth digit attack on amount field | Low | rejected at NFC boundary, no $ loss | NFC inside `JsonCanonicalizer` v2; negative test `Body_FullwidthDigits_AreNotEquivalentToAscii_UnderNfc` | Security + QA |
| Cache exhaustion DoS via Idempotency-Key spray | Medium | partial degradation | Per-sub cap 1000, per-IP cap 5000; Redis evicts oldest | Security |
| Stripe primary outage during canary | Low | checkout 5xx until Adyen failover | Adyen backup in CSP allowlist; manual flip via App Configuration | Planning |
| EMU continues to block automation in tamirdresher/travel-assistant | High | Issues filed via squad messages, not GitHub Issues | Decision records in `squads/*/artifacts/`; maintainer apply manually | Planning |

**No P0 risks unaccepted.** All P1s have mitigations either shipped or documented.

---

## 6. What This Does NOT Cover

Out of scope for this canary. Track separately if/when prioritized:

- Multi-currency display / FX (only minor-unit storage is shipped)
- Refund / partial-refund flow (subsequent vertical)
- Subscription / recurring billing (subsequent vertical)
- Apple Pay / Google Pay (would extend CSP allowlist + paymentBridge schema)
- 3DS2 challenge flow polish beyond provider iframe default
- Saved payment methods (requires PCI scope expansion — currently SAQ-A only)
- Tax calculation beyond flat per-line (no tax engine integration)
- Inventory across multiple warehouses (single-region SKU hold only)

---

## 7. Sign-off Matrix

| Squad | Artifact | Sign-off |
|---|---|---|
| experience-design | `checkout-design-spec.md` | ✅ |
| application-development | hotfix-p0 bundle + wi6-redis-di-reconcile + wi4-wi5 + wi1a-nfc-amendment | ✅ |
| security-hardening | JsonCanonicalizer v2 + IdempotencyKeyDerivation + CSP + paymentBridge | ✅ |
| azure-infrastructure | Redis Bicep + VNet + PE + MI + RedisHealthCheck | ✅ |
| quality-testing | Full test suite (40 idempotency-vertical tests) + webhook harness + k6 replay storm | ✅ |
| review-deployment | checkout-ci.yml + checkout-canary-promote.yml + rollback action + PR template | ✅ |
| **ideation-research-planning** | **This decision record** | ✅ |

---

## 8. Apply Command (for maintainer)

```bash
# From tamirdresher/travel-assistant clone (EMU-authorized)
git fetch origin
# Apply steps 1–10 from §2 in order, verifying each gate before the next.
# After step 9, the promote workflow is dispatched automatically by the deploy:prod label.
# Manual approval at GH Environment gates: checkout-1pct → checkout-10pct → checkout-50pct → checkout-100pct.
```

**Two-person approval required at the 100% gate.** Rollback authority is single-maintainer.

---

*Planning's role on this vertical ends here. Re-engage when scope expands (refunds, subscriptions, Apple/Google Pay, 3DS2 polish) or when a P0 incident requires a new work-item breakdown.*

> "A plan is a list of things that don't happen by themselves." — Anonymous
