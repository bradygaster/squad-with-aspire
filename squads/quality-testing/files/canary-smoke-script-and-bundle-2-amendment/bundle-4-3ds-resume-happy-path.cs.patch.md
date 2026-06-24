# Bundle 4 — Confirm3DSResumeHappyPath test (new)

**Trigger:** app-dev `bbc2faa` answering Q-CO-3 — 3DS shape is redirect-out-and-return only (NOT iframe, NOT same-page). Single E2E shape across Stripe + Adyen.

## Contract under test

`POST /api/checkout/{checkoutSessionId}/confirm` with a payment method requiring 3DS → response:

```json
{ "state": "ActionRequired",
  "actionRequired": {
    "type": "redirect",
    "url": "https://provider.example/3ds/challenge?token=...",
    "returnUrl": "/checkout/{checkoutSessionId}/review?resume=1"
  }
}
```

Resume flow: client navigates to `actionRequired.url`, provider redirects back to `returnUrl` with provider-issued query (`?resume=1&provider_session=...`). Polling `/status` then returns `state: "Confirmed"`.

## File: `tests/TravelAssistant.Api.Tests/Checkout/Confirm3DSResumeHappyPathTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static TravelAssistant.Api.Checkout.CheckoutDebugSeamContract;
using static TravelAssistant.Api.Checkout.CheckoutWebhookEnvelope;

namespace TravelAssistant.Api.Tests.Checkout;

public class Confirm3DSResumeHappyPathTests : IClassFixture<CheckoutApiFixture>
{
    private readonly CheckoutApiFixture _fx;
    public Confirm3DSResumeHappyPathTests(CheckoutApiFixture fx) => _fx = fx;

    [Theory]
    [InlineData("stripe", "pm_card_threeDSecure2Required")]
    [InlineData("adyen",  "scheme_3ds2_mandatory_test")]
    public async Task Confirm_With3DSPaymentMethod_ReturnsActionRequiredRedirect_ThenResumesToConfirmed(
        string provider, string paymentMethodId)
    {
        // ARRANGE — seed a cart + reach Review state
        var session = await _fx.NewSessionAtReviewAsync(provider, paymentMethodId);

        // ACT 1 — initial confirm returns ActionRequired
        var confirm = await _fx.Client.PostAsJsonAsync(
            $"/api/checkout/{session.Id}/confirm",
            new { idempotencyKey = Guid.NewGuid().ToString() });

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var body = await confirm.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("ActionRequired", body.GetProperty("state").GetString());

        var action = body.GetProperty("actionRequired");
        Assert.Equal("redirect", action.GetProperty("type").GetString());

        var url = action.GetProperty("url").GetString();
        Assert.False(string.IsNullOrEmpty(url));
        Assert.StartsWith("https://", url, StringComparison.Ordinal); // R5: absolute, HTTPS

        var returnUrl = action.GetProperty("returnUrl").GetString();
        Assert.Equal($"/checkout/{session.Id}/review?resume=1", returnUrl); // exp-design b47c68f spec

        // ASSERT 1 — state machine pinned: NOT Confirming yet (provider hasn't authorized)
        var statusBeforeResume = await _fx.Client.GetFromJsonAsync<JsonElement>(
            $"/api/checkout/{session.Id}/status");
        Assert.Equal("ActionRequired", statusBeforeResume.GetProperty("state").GetString());

        // ACT 2 — simulate provider callback (test seam: webhook post)
        await _fx.SeedProviderWebhookAsync(provider, new {
            type    = Events.PaymentAuthorized,
            sessionId = session.Id,
            providerSessionId = "ps_test_3ds_success"
        });

        // ACT 3 — poll /status until Confirmed (cap 5s, 250ms cadence)
        var final = await _fx.PollStatusUntilTerminalAsync(session.Id, TimeSpan.FromSeconds(5));

        // ASSERT 2 — terminal state is Confirmed, no double-charge
        Assert.Equal("Confirmed", final.GetProperty("state").GetString());

        var provCount = await _fx.Client.GetFromJsonAsync<JsonElement>(
            $"{Routes.ProviderCallCount}/{session.Id}");
        Assert.Equal(1, provCount.GetProperty("callCount").GetInt32()); // bundle 4 anti-double-charge invariant
    }
}
```

## Discipline gates

- Uses `static using CheckoutDebugSeamContract` (Routes constants) — no `_debug/*` magic strings.
- Uses `Events.PaymentAuthorized` from `CheckoutWebhookEnvelope.Events` — eligible for bundle 1 enumeration guard.
- No iframe assertions (Q-CO-3 redirect-only). If iframe code paths ever appear → bundle 6 (`iframeBoundaryOnly:true`) page config refuses at schema layer.

## Open assertions deferred to bundle 8

- Abandoned vs failed distinction (provider timeout on return-from-3DS) → bundle 8 once `Q-CO-DEV-3DS` abandoned-vs-failed Q closes with exp-design.
