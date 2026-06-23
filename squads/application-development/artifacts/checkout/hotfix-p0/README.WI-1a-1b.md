# Checkout Hotfix P0 — WI-1a + WI-1b amendment (SEC-CHK-007 bindings + CSP wiring)

> Amends the original `fix/checkout-idempotency-p0` bundle. Apply on top of the
> existing hotfix-p0/ artifacts (commit `6c3eb41`). EMU still blocks direct push
> to `tamirdresher/travel-assistant`; deliver via maintainer apply.

## What this adds

| WI | File(s) | Purpose |
|----|---------|---------|
| **WI-1a R1** | `src/IdempotencyStore.v2.cs` | `CryptographicOperations.FixedTimeEquals` on **raw 32-byte** SHA-256 hashes (not ASCII hex strings). Kills the residual timing oracle on the hex path. |
| **WI-1a R2** | `src/IdempotencyStore.v2.cs` + `src/CheckoutEndpoints.confirm.v2.cs` | `IIdempotencyKeyDeriver` seam. Cache key = `SHA-256("sub:<sub>:"<key>")` or `SHA-256("guest:<sid>:"<key>")` — never raw `Idempotency-Key`. Cross-tenant disclosure impossible. |
| **WI-1a R3** | `src/CheckoutEndpoints.confirm.v2.cs` | Body canonicalization via `JsonCanonicalizer.CanonicalizeUtf8` (RFC 8785 JCS, shipped by security-hardening-squad). No hand-rolled sorted-key serializer. |
| **WI-1a A1** | `src/CheckoutEndpoints.confirm.v2.cs` | `/checkout/confirm` TTL = **15 min** (matches inventory hold). `/checkout/session` keeps 24h. |
| **WI-1a T13** | `src/IdempotencyStore.v2.cs` + `src/CheckoutEndpoints.confirm.v2.cs` | Per-subject (1000) + per-IP (5000) entry caps; 1001st distinct key → **429 + `Retry-After`**. Config-driven via `IdempotencyOptions`. |
| **WI-1b CSP** | `apps-web/middleware.ts` + `apps-web/app/api/csp-report/route.ts` | Registers `cspMiddleware` from security squad's `src/middleware/csp.ts` for `/checkout/*` + `/csp-report` sink returning 204. |
| **WI-1b bridge** | `apps-web/app/checkout/payment/page.tsx` + `PaymentBridgeClient.tsx` | Server-renders 256-bit single-use nonce from `x-payment-bridge-nonce` response header; mounts `mountPaymentBridge` from security squad's `src/checkout/paymentBridge.ts`. Server reconciles amount/currency/orderId via provider API — iframe is never trusted. |
| **Tests** | `tests/IdempotencyWi1aTests.cs` | Pins R1 (FixedTimeEquals), R2 (per-sub/per-guest derived key), R3 (key-order + whitespace invariance), T13 (subject + IP caps), preserve-status replay. |

## Required cross-squad files to copy alongside

These ship from `squads/security-hardening/artifacts/checkout-sec-reference/` and
must land in the same PR:

| Source (security-hardening) | Destination in repo |
|---|---|
| `IdempotencyKeyDerivation.cs` | `src/TravelAssistant.Api/Checkout/Security/IdempotencyKeyDerivation.cs` |
| `JsonCanonicalizer.cs`        | `src/TravelAssistant.Api/Checkout/Security/JsonCanonicalizer.cs` |
| `IdempotencyKeyDerivationTests.cs` | `tests/TravelAssistant.Api.Tests/Checkout/Security/IdempotencyKeyDerivationTests.cs` |
| `apps/web/src/middleware/csp.ts` | `apps/web/src/middleware/csp.ts` |
| `apps/web/src/checkout/paymentBridge.ts` | `apps/web/src/checkout/paymentBridge.ts` |

## Apply recipe (non-EMU maintainer)

```bash
git checkout fix/checkout-idempotency-p0     # or create from main and apply hotfix-p0/ first

# 1. Security squad reference helpers
mkdir -p src/TravelAssistant.Api/Checkout/Security
cp squads/security-hardening/artifacts/checkout-sec-reference/IdempotencyKeyDerivation.cs src/TravelAssistant.Api/Checkout/Security/
cp squads/security-hardening/artifacts/checkout-sec-reference/JsonCanonicalizer.cs       src/TravelAssistant.Api/Checkout/Security/
mkdir -p tests/TravelAssistant.Api.Tests/Checkout/Security
cp squads/security-hardening/artifacts/checkout-sec-reference/IdempotencyKeyDerivationTests.cs tests/TravelAssistant.Api.Tests/Checkout/Security/

# 2. App-dev WI-1a (replace v1 files)
cp squads/application-development/artifacts/checkout/hotfix-p0/src/IdempotencyStore.v2.cs            src/TravelAssistant.Api/Checkout/IdempotencyStore.cs
cp squads/application-development/artifacts/checkout/hotfix-p0/src/CheckoutEndpoints.confirm.v2.cs   src/TravelAssistant.Api/Checkout/CheckoutEndpoints.confirm.cs
cp squads/application-development/artifacts/checkout/hotfix-p0/tests/IdempotencyWi1aTests.cs        tests/TravelAssistant.Api.Tests/Checkout/

# 3. App-dev WI-1b (apps/web wiring) — copy security squad's csp.ts + paymentBridge.ts first
# (those are in security-hardening's session files — patch bundle includes them).
cp squads/application-development/artifacts/checkout/hotfix-p0/apps-web/middleware.ts                apps/web/middleware.ts
mkdir -p apps/web/app/api/csp-report apps/web/app/checkout/payment
cp squads/application-development/artifacts/checkout/hotfix-p0/apps-web/app/api/csp-report/route.ts  apps/web/app/api/csp-report/route.ts
cp squads/application-development/artifacts/checkout/hotfix-p0/apps-web/app/checkout/payment/page.tsx                apps/web/app/checkout/payment/page.tsx
cp squads/application-development/artifacts/checkout/hotfix-p0/apps-web/app/checkout/payment/PaymentBridgeClient.tsx apps/web/app/checkout/payment/PaymentBridgeClient.tsx

# 4. Build + test
dotnet test tests/TravelAssistant.Api.Tests/
cd apps/web && pnpm install && pnpm test && pnpm build
```

## Acceptance — what to verify before merge

- [ ] `IdempotencyWi1aTests` 8/8 green
- [ ] Security squad's `IdempotencyKeyDerivationTests` green
- [ ] QA's 8 new idempotency cases green (cross-user replay isolation, guest→auth rebind, timing-difference, JCS invariance ×3, per-sub cap, per-IP cap)
- [ ] QA's 4 CSP/postMessage E2E smokes green
- [ ] BUG-1 + BUG-2 contract tests (`IdempotencyRegressionTests`) un-skipped and green
- [ ] CSP header present on `GET /checkout/payment` response (frame-ancestors 'none', frame-src allowlist, nonce-based script-src)
- [ ] `POST /csp-report` returns 204
- [ ] `Retry-After: 60` set on 429 responses from cap-exceeded path

## Sequencing

WI-1a + WI-1b ship in **the same `fix/checkout-idempotency-p0` PR** as the original
hotfix. WI-4 (inventory hold) and WI-5 (webhook idempotency) remain separate PRs.

Canary remains held at 0% until azure-infrastructure-squad's WI-6 distributed
idempotency store lands and the `IIdempotencyStore` Redis impl ships.

— Bennett, application-development-squad
