# NEXT VERTICAL — Saved Payment Methods v1

**Owner:** ideation-research-planning-squad
**Filed:** 2026-06-24
**Status:** Queued (depends on refunds v1 ship)
**Branch target:** `tamir/squad-fixes`
**Repo:** tamirdresher/travel-assistant (EMU blocks gh issue creation — this artifact IS the issue)

---

## 1. Problem Statement

Returning users re-enter card details on every checkout. Checkout abandonment on the
payment step is the single largest funnel drop (per checkout analytics, dispatched
event `checkout.step.abandoned step=payment`). Saved payment methods (SPM) reduce
re-entry friction for confirmed buyers and unblock the v2 wallet verticals
(Apple Pay / Google Pay tokenization needs the same vault primitive).

**v1 scope:** save **one** card per user during a successful checkout, list saved
methods on a profile page, delete a saved method, reuse a saved method at the
next checkout.

**Out of scope for v1** (explicitly punted to v2+):
- Multiple cards per user (v1 = exactly 0 or 1)
- Default card selection UI (single card has no concept of default)
- Card nickname / labeling
- Apple Pay / Google Pay tokenization
- Bank transfer / SEPA / iDEAL
- Card update flow (CVV re-collect on expiry)
- 3DS2 step-up on saved-card reuse
- Cross-device sync (already free — saved method is server-side keyed by `sub`)
- Admin-side "force unlink" tooling
- Saved method portability / export

**PCI scope guardrail:** v1 **must remain SAQ-A**. We never see, transit, or
store PAN. Vaulting is delegated to the payment provider (Stripe `SetupIntent` →
`PaymentMethod` ID, or Adyen tokenization endpoint). We store ONLY the opaque
provider token + display metadata (last4, brand, exp_month, exp_year). If any
v1 work item suggests touching PAN, **escalate to security and stop**.

---

## 2. Work Items

| ID | Squad | Title | Depends on |
|----|-------|-------|------------|
| WI-SPM-1 | application-development | `POST /api/payment-methods` (vault opt-in at checkout) + `GET /api/payment-methods` (list, 0–1) + `DELETE /api/payment-methods/{methodId}` | WI-SPM-6 |
| WI-SPM-2 | application-development | Wire SPM reuse into checkout: `POST /api/checkout` accepts `paymentMethodId` (existing vault) **or** raw provider token (new card path) — mutually exclusive | WI-SPM-1 |
| WI-SPM-3 | experience-design | Checkout opt-in checkbox spec + Profile → Payment Methods page spec + delete confirmation modal spec | — |
| WI-SPM-4 | application-development | Frontend: opt-in checkbox at checkout (default unchecked), Payment Methods profile page, saved-card pill at checkout payment step | WI-SPM-2, WI-SPM-3 |
| WI-SPM-5 | security-hardening | SPM threat model: vault token ownership (IDOR on `methodId`), opt-in dark-pattern review, delete-is-actually-deleted assertion, audit log requirements, GDPR DSR (right-to-erasure) hook | — |
| WI-SPM-6 | azure-infrastructure | Cosmos container `payment-methods` partitioned on `/userId`, RU/s ceiling, additive composite index `userId asc / createdAt desc`, no PII in indexed fields | — |
| WI-SPM-7 | quality-testing | Contract tests (vault lifecycle), IDOR matrix (Eve cannot read/delete Alice's method), opt-in/opt-out matrix, provider-token-replay test, GDPR erasure test, deletion-actually-revokes-at-provider test | WI-SPM-1, WI-SPM-2, WI-SPM-5 |
| WI-SPM-8 | review-deployment | CI greps (no PAN in repo, vault token never logged, IDOR-404-not-403 contract), feature flag `saved_payment_methods_v1_enabled`, internal-tenant → 1% → 10% → 100% (no canary infra-gate required, flag-gated, all reads/writes behind flag) | All |

**Critical path:** WI-SPM-6 → WI-SPM-1 → WI-SPM-2 → (WI-SPM-4 ∥ WI-SPM-5) → WI-SPM-7 → WI-SPM-8
**Day-1 parallelizable:** WI-SPM-3 (UX), WI-SPM-5 (security threat model), WI-SPM-6 (infra)

---

## 3. API Contracts (binding)

### 3.1 Vault during checkout (opt-in)

`POST /api/checkout` request gains optional field:

```json
{
  "cartId": "...",
  "providerToken": "tok_visa_xxx",
  "savePaymentMethod": false
}
```

- `savePaymentMethod: true` → on successful charge, server calls provider's
  vault endpoint, stores returned vault token + metadata, links to `sub`.
- `savePaymentMethod: false` (default) → existing path, no vault call.
- Vault failure after successful charge **does not roll back the charge** —
  log as `spm.vault_failure_post_charge` telemetry, return checkout success
  with `paymentMethodSaved: false` in response.

### 3.2 List saved methods

`GET /api/payment-methods` → `200`

```json
{
  "paymentMethods": [
    {
      "methodId": "pm_01H...",        // ULID, opaque, our ID — NEVER expose provider token
      "brand": "visa",                  // enum: visa|mastercard|amex|discover|other
      "last4": "4242",
      "expMonth": 12,
      "expYear": 2028,
      "createdAt": "2026-06-24T...Z"
    }
  ]
}
```

- Response is **always an array** (length 0 or 1 in v1). Frontend renders
  empty state for `[]`, list for `[item]`. Same contract scales to v2 multi-card.
- IDOR-safe: only methods where `userId == sub` (from JWT) are returned.
- No pagination needed at v1 (max 1 item). Add cursor pagination in v2.

### 3.3 Delete saved method

`DELETE /api/payment-methods/{methodId}` → `204` on success

- IDOR-safe: returns **404** (not 403) if `methodId` does not belong to caller —
  prevents enumeration oracle. Same pattern as orders/refunds.
- Server MUST call provider's revoke endpoint (Stripe `PaymentMethod.detach`,
  Adyen `disable`) **before** marking deleted locally. If provider call fails,
  return **502 Bad Gateway** and leave local state untouched — user retries.
- Deletion is **synchronous and permanent**. No soft-delete in v1 (GDPR-safer).
  Audit log entry written to `payment-method-audit` container (7yr retention,
  immutable RBAC — mirrors refunds-audit pattern).

### 3.4 Reuse at checkout

`POST /api/checkout` accepts **one of**:

```json
{ "providerToken": "tok_..." }                  // new card path
{ "paymentMethodId": "pm_01H..." }              // saved card path
```

- **Mutually exclusive** — both present → `400 Bad Request` code
  `payment_method_ambiguous`. Neither present → `400` code
  `payment_method_required`.
- `paymentMethodId` not owned by caller → `404 Not Found` (IDOR-safe), not 403.
- Saved-card reuse path does **NOT** re-prompt for CVV in v1. Provider handles
  liability shift per their saved-card policy. (v2 may add CVV re-collect for
  high-value carts — out of scope here.)

---

## 4. Data Model

### 4.1 Cosmos `payment-methods` container

- **Partition key:** `/userId`
- **Indexed paths:** `/userId/?`, `/createdAt/?` (composite for sort)
- **Excluded paths:** `/*` (default exclude — only index what we query)
- **Unique key constraint:** `["/userId"]` enforces v1 single-card invariant
  at the database layer. POST returns **409 Conflict** code
  `payment_method_already_exists` if user already has one. Frontend should
  delete-then-save (two-step) for replace.

Document shape:

```json
{
  "id": "pm_01H...",                  // ULID, primary key
  "userId": "auth0|abc123",            // sub claim, partition key
  "providerName": "stripe",            // enum: stripe|adyen
  "providerVaultToken": "pm_xxx",      // SECRET — never logged, never returned to client
  "brand": "visa",
  "last4": "4242",
  "expMonth": 12,
  "expYear": 2028,
  "createdAt": "2026-06-24T...Z",
  "_etag": "..."
}
```

### 4.2 `payment-method-audit` container

- Append-only, immutable RBAC (mirrors `refund-audit`).
- Retention 7 years.
- Records: `created | reused | deleted | vault_failure_post_charge`.
- Contains: `methodId`, `userId`, `event`, `timestamp`, `correlationId`.
  **Never contains:** `providerVaultToken`, PAN, last4 (last4 is on the live
  doc; audit log captures the event, not the data).

---

## 5. Security Requirements (SEC-SPM-*)

| ID | Requirement |
|----|-------------|
| SEC-SPM-001 | Provider vault token **never serialized to client response**. Greppable: no field named `providerVaultToken` in any API response DTO. |
| SEC-SPM-002 | Vault token never logged. Structured-log redactor masks any field matching `provider*Token` or `pm_*` pattern. |
| SEC-SPM-003 | IDOR on `methodId`: returns 404 not 403 for non-owned methods. Asserted by Eve-vs-Alice contract test. |
| SEC-SPM-004 | Opt-in checkbox at checkout is **default unchecked**. Dark-pattern review by experience-design + security joint sign-off. Telemetry `spm.opt_in_rate` monitored — sudden spike triggers UX audit. |
| SEC-SPM-005 | Delete actually revokes at provider before local delete. Asserted by integration test against provider sandbox. |
| SEC-SPM-006 | GDPR DSR (Data Subject Request — right to erasure) hook: existing `DELETE /api/users/me` cascades to delete all `payment-methods` for that `sub` AND calls provider revoke for each. Async OK (saga), but must complete within 30 days per GDPR. |
| SEC-SPM-007 | CSP `connect-src` allowlist updated for any new provider endpoints (Stripe vault domains). |
| SEC-SPM-008 | Rate limit: `POST /api/payment-methods` is implicit via `POST /api/checkout` rate limit (1000/sub/24h). `DELETE` standalone: 50/sub/24h (deletion abuse less attractive but prevent thrash). `GET`: 200/sub/24h. |

---

## 6. Test Strategy (delegated to quality-testing-squad WI-SPM-7)

**Pyramid target:** ~24 unit / 18 integration / 3 E2E / 1 load / 5 gate tests.

**P0 gates (canary-blocking — copy refunds GATE-RFD pattern):**

- **GATE-SPM-01:** Provider vault token never appears in any response DTO
  (greps all controllers + DTOs).
- **GATE-SPM-02:** IDOR returns 404 not 403 on cross-tenant access.
- **GATE-SPM-03:** Delete calls provider revoke before local delete (asserted
  via provider sandbox spy).
- **GATE-SPM-04:** Unique key constraint enforces single card per user (POST
  returns 409 on second insert).
- **GATE-SPM-05:** Opt-in default = unchecked (asserted via DOM contract test
  on rendered checkout page).

**Dev seams app-dev must expose (mirrors refunds bundle):**

- `IProviderVaultClient` (interface for Stripe/Adyen vault calls — FakeProviderVaultClient for tests)
- `SeedSavedPaymentMethod(userId, vaultToken)` test helper
- `_debug/payment-method-count/{userId}` (gated by `ASPNETCORE_ENABLE_TEST_AUTH=1`)
- `_debug/last-provider-revoke-call` (gated by `ASPNETCORE_ENABLE_TEST_AUTH=1`)

---

## 7. Rollout

**No infra canary gate required** (no new provider integration risk — vault
endpoints are existing Stripe/Adyen APIs we already call).

**Flag-gated rollout** via `saved_payment_methods_v1_enabled`:

1. Internal tenant 100% (1 week soak — zero P0/P1 + zero `spm.vault_failure_post_charge`)
2. 1% production (3 days)
3. 10% production (3 days)
4. 100% production

**Rollback:** flag flip <60s. In-flight saves complete (provider call already
made — local persistence is the last step). Already-saved methods remain usable
during rollback (delete still works, list still works) — only the checkout
opt-in checkbox is hidden.

---

## 8. Cost Delta

- Cosmos `payment-methods` container: ~400 RU/s baseline + ~100 RU/s burst
  = **~$32/mo**
- Cosmos `payment-method-audit` container: append-only ~200 RU/s = **~$16/mo**
- Total: **+$48/mo** on top of $614 refunds baseline = **$662/mo total infra**.

---

## 9. Out of Scope (frozen — v2 backlog)

| Item | Why v2 |
|------|--------|
| Multiple cards per user | Default-card UI + selection-at-checkout UX is a separate vertical |
| Card nicknames | UI complexity without funnel impact data |
| CVV re-collect on saved-card reuse | Provider handles liability; revisit only if fraud rate rises |
| Apple Pay / Google Pay | Separate wallet vertical, depends on this vault primitive shipping |
| Card update (expiry) | Provider auto-update services (Stripe Card Updater) cover most cases |
| 3DS2 step-up | Separate compliance vertical |
| Bank transfer / SEPA / iDEAL | EU-market vertical |
| Saved-method export / portability | GDPR DSR portability is broader than just SPM |
| Admin force-unlink | Support tooling vertical |
| Cross-device sync UX | Already free server-side; UX confirmation messaging deferred |

**Hard guardrail:** if implementation pressure suggests adding any of the
above "while we're in there," route back to ideation-research-planning.
This is how SAQ-A becomes SAQ-D becomes a 6-month PCI audit.

---

## 10. Apply Order

1. **WI-SPM-6** (infra: Cosmos container + audit container) — day 1 parallel
2. **WI-SPM-3** (UX spec) — day 1 parallel
3. **WI-SPM-5** (security threat model + sign-off on SEC-SPM-001..008) — day 1 parallel
4. **WI-SPM-1** (vault + list + delete endpoints) — after WI-SPM-6
5. **WI-SPM-2** (checkout integration: paymentMethodId path + mutual-exclusion validation) — after WI-SPM-1
6. **WI-SPM-7** (QA bundle: contract + IDOR + GDPR erasure + revoke-before-delete) — after WI-SPM-1, WI-SPM-2, WI-SPM-5
7. **WI-SPM-4** (frontend: opt-in checkbox + profile page + saved-card pill) — after WI-SPM-2, WI-SPM-3
8. **WI-SPM-8** (CI greps + flag-gated rollout) — gates everything

---

## 11. Open Questions (resolve before WI-SPM-1 kickoff)

1. **Provider choice for v1 vault:** Stripe primary (already wired for checkout)
   — confirm. Adyen vault deferred to v2 unless dual-vault is hard requirement.
2. **GDPR DSR cascade timeline:** existing `DELETE /api/users/me` is sync today
   for orders; SPM revoke is async (provider call may take seconds). Acceptable
   to make user-deletion async end-to-end? Recommend yes (already best practice).
3. **Telemetry funnel definition:** experience-design owns. Need
   `spm.opt_in_shown`, `spm.opt_in_checked`, `spm.vault_success`,
   `spm.vault_failure_post_charge`, `spm.reuse_at_checkout`, `spm.delete_clicked`.

Standing by for sign-off; will dispatch WIs once refunds v1 lands.

— ideation-research-planning-squad

> "Make it work, make it right, make it fast — and don't store the card number." — Kent Beck (paraphrased)
