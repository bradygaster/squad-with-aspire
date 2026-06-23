# `fix/checkout-idempotency-p0` — Hotfix bundle

Addresses **WI-1**, **WI-2**, **WI-3** from ideation-research-planning-squad's QA escalation. WI-4 (inventory hold) and WI-5 (webhook idempotency) ship in separate PRs per the sequencing instruction.

## Files

| Path in this bundle | Destination in `tamirdresher/travel-assistant` | Action |
|---|---|---|
| `src/IdempotencyStore.cs` | `src/TravelAssistant.Api/Checkout/IdempotencyStore.cs` | **Replace** |
| `src/Money.cs` | `src/TravelAssistant.Api/Checkout/Money.cs` | **New** |
| `src/CheckoutEndpoints.confirm.cs` | `src/TravelAssistant.Api/Checkout/CheckoutEndpoints.confirm.cs` | **New** (replaces `/confirm` handler — remove the old `grp.MapPost("/confirm", ...)` block from `CheckoutEndpoints.cs` and call `app.MapCheckoutConfirmEndpoint()` from `Program.cs` alongside `MapCheckoutEndpoints()`) |
| `tests/IdempotencyAndMoneyHotfixTests.cs` | `tests/TravelAssistant.Api.Tests/Checkout/IdempotencyAndMoneyHotfixTests.cs` | **New** |

## What changed and why

### WI-1 — body-hashed idempotency, 422 on mismatch
- `IIdempotencyStore` is now keyed by `(key, bodyHash, subjectClaim)`.
- Body hash = SHA-256 of canonical JSON (sorted keys, no whitespace).
- Comparison is constant-time (`CryptographicOperations.FixedTimeEquals`).
- Mismatch returns **422 `problem+json`** with `type=idempotency-key-conflict` per draft-ietf-httpapi-idempotency-key-header.
- TTL: 24h. Cache binds to JWT `sub` claim when present (per security-hardening-squad SEC-CHK-007 ask).

### WI-2 — preserve original status code on replay
- Cache entry now stores `{ statusCode, body }`. Replay uses `Results.Text(json, "application/json", statusCode: cached.StatusCode)` — 402, 409, 410, 422 round-trip correctly.

### WI-3 — Money as integer minor units
- New `Money { long MinorUnits, string CurrencyCode }` value type.
- Per-currency exponent table (USD=2, EUR=2, JPY=0, BHD=3, KWD=3, etc.).
- `Money.FromDecimalMajor` rejects over-precision (JPY-with-cents → 400; BHD-with-4dp → 400).
- `MoneyJsonConverter` ensures wire format is `{ "minorUnits": 1234, "currencyCode": "USD" }` — never decimal.
- **Migration:** all `decimal AmountCents` / `decimal UnitPriceCents` / `decimal SubtotalCents` in `CheckoutModels.cs` need to switch to `Money`. This bundle ships the type and tests — the model migration is a 1-file mechanical change in the same PR.

### WI-3 — concurrency
- `TryReserve` marks a key as in-flight. A second request for the same `(key, bodyHash)` while the first is processing returns **409 `conflict-in-progress`**. Reservation is released on completion or exception.

## Coordination

- **security-hardening-squad SEC-CHK-007:** Body hash bound to `sub` claim ✅. Constant-time compare ✅. TTL=24h ✅. Please sign off in the decisions inbox.
- **quality-testing-squad:** Tests file matches the contract you scaffolded. Un-skip `IdempotentReplay_*` and `BodyMismatch_Returns422` once these endpoints land — they should go green.
- **review-deployment-squad:** PR template checklist additions already accepted. Do not promote until contract tests green.

## EMU delivery note

Branch can't be pushed from this account (Enterprise Managed User → `tamirdresher/travel-assistant` returns 403). Maintainer applies via:

```
cd /path/to/travel-assistant
git checkout -b fix/checkout-idempotency-p0
# copy files per the table above
git add -A
git commit -m "fix(checkout): body-hashed idempotency, status-preserving replay, Money minor units (WI-1, WI-2, WI-3)"
git push -u origin fix/checkout-idempotency-p0
gh pr create --fill --base main
```

— Bennett (Backend), application-development-squad
