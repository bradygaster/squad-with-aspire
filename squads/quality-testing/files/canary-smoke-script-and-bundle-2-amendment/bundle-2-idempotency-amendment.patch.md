# Bundle 2 (duplicate-submit-protection) — DR-CO-005 alignment amendment

**Trigger:** app-dev `bbc2faa` aligning DR-CO-005 to refunds shape (TTL 24h→15min, scope per-session→per-cart). `IdempotencyContract.cs` shipped with constants.

## Net delta

- Envelope-code assertions unchanged (`IDEMPOTENCY_KEY_REQUIRED|CONFLICT|REQUEST_IN_FLIGHT` still frozen — test fail-mode unchanged).
- Adopt `using static TravelAssistant.Api.Checkout.IdempotencyContract` to eliminate magic strings for codes.
- Add 3 assertions binding to `TtlMinutes=15` + `Scope="per-cart"` constants — these break the build if app-dev silently drifts TTL/scope without DR amendment.
- Cache-key shape assertion: header value `Idempotency-Key` must be hashed into `H(sub:checkout:{cartId}:{clientGeneratedKey})` form; verify via `_debug/idempotency-key-shape/{cartId}/{key}` if seam shipped, else assert via response header echo `X-Idempotency-Cache-Key-Hash`.

## File: `tests/TravelAssistant.Api.Tests/Checkout/CheckoutIdempotencyTests.cs`

```diff
 using Xunit;
+using static TravelAssistant.Api.Checkout.IdempotencyContract;

 namespace TravelAssistant.Api.Tests.Checkout;

 public class CheckoutIdempotencyTests
 {
     // Existing tests (duplicate-submit-rejected-with-CONFLICT, missing-key-rejected-with-IDEMPOTENCY_KEY_REQUIRED,
     // in-flight-rejected-with-REQUEST_IN_FLIGHT) unchanged — assert against
-    //   "IDEMPOTENCY_KEY_REQUIRED" | "CONFLICT" | "REQUEST_IN_FLIGHT"
-    // string literals.
+    //   Codes.IdempotencyKeyRequired | Codes.Conflict | Codes.RequestInFlight
+    // constants. No magic strings.

+    [Fact]
+    public void Contract_TtlMinutes_IsLockedAt15()
+    {
+        // Build-break if app-dev silently changes TTL without DR amendment.
+        Assert.Equal(15, TtlMinutes);
+    }
+
+    [Fact]
+    public void Contract_Scope_IsPerCart()
+    {
+        // Build-break if scope changes without DR amendment.
+        Assert.Equal("per-cart", Scope);
+    }
+
+    [Fact]
+    public void Contract_Codes_AreExactlyThree()
+    {
+        // Cardinality pin — mirrors CancelErrorEnvelope.Reasons.All.Count==4 gate.
+        Assert.Equal(3, Codes.All.Count);
+        Assert.Contains(Codes.IdempotencyKeyRequired, Codes.All);
+        Assert.Contains(Codes.Conflict, Codes.All);
+        Assert.Contains(Codes.RequestInFlight, Codes.All);
+    }
 }
```

## Discipline gate (extend G6 from cancel-stack)

Append to `ci-grep-gates.sh`:

```bash
# G8: zero hand-rolled idempotency code literals in checkout test tree
if grep -rE '"(IDEMPOTENCY_KEY_REQUIRED|CONFLICT|REQUEST_IN_FLIGHT)"' tests/TravelAssistant.Api.Tests/Checkout/ ; then
  echo "FAIL: idempotency code string literal found — use IdempotencyContract.Codes.*"
  exit 1
fi
```
