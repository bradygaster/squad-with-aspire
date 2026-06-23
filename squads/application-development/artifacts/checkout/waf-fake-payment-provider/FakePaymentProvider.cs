// FakePaymentProvider.cs
// Drop into tests/TravelAssistant.Api.Tests/Checkout/Fakes/
//
// Deterministic IPaymentProvider for WebApplicationFactory<Program> tests.
// Token contract matches QA's TestPaymentTokens.cs (wi-1a-followup bundle).
//
// CACHED outcomes (terminal — flow through IdempotencyStore so replays preserve status):
//   tok_visa_ok           → 200 OK     (success)
//   tok_chargeDeclined    → 402 Payment Required   (declined)
//   tok_declined          → 402 Payment Required   (declined alias)
//   tok_fraud_reject      → 403 Forbidden          (fraud reject)
//
// NON-CACHED outcomes (throw — pipeline returns without writing IdempotencyStore entry,
// so retries get a fresh attempt, matching v2 IdempotencyStore.Hit() semantics):
//   tok_gateway_timeout   → throws PaymentGatewayTimeoutException → 504
//   tok_invalid           → throws PaymentValidationException     → 400
//
// Any other token → 200 OK with a synthetic intent id (default success path for
// tests that don't care about the negative branch).

using System.Threading;
using System.Threading.Tasks;
using TravelAssistant.Api.Checkout;

namespace TravelAssistant.Api.Tests.Checkout.Fakes;

public sealed class FakePaymentProvider : IPaymentProvider
{
    public Task<PaymentResult> ConfirmAsync(
        PaymentConfirmRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.PaymentToken ?? string.Empty;

        return token switch
        {
            "tok_visa_ok"          => Task.FromResult(PaymentResult.Succeeded(intentId: $"pi_test_{request.OrderId}")),

            "tok_chargeDeclined"   => Task.FromResult(PaymentResult.Declined(
                                          statusCode: 402,
                                          code: "card_declined",
                                          message: "Your card was declined.")),

            "tok_declined"         => Task.FromResult(PaymentResult.Declined(
                                          statusCode: 402,
                                          code: "card_declined",
                                          message: "Your card was declined.")),

            "tok_fraud_reject"     => Task.FromResult(PaymentResult.Declined(
                                          statusCode: 403,
                                          code: "fraudulent",
                                          message: "Suspected fraud.")),

            "tok_gateway_timeout"  => throw new PaymentGatewayTimeoutException(
                                          "Upstream payment gateway did not respond within the deadline."),

            "tok_invalid"          => throw new PaymentValidationException(
                                          code: "token_invalid",
                                          message: "Payment token is malformed or expired."),

            _                      => Task.FromResult(PaymentResult.Succeeded(intentId: $"pi_test_{request.OrderId}")),
        };
    }
}
