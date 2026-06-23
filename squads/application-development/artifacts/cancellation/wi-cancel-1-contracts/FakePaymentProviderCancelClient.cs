// Test seam for QA's WI-CANCEL-1 integration suite (required per DR-CANCEL-001).
// Mirrors FakePaymentProvider/FakeRefundPaymentProvider patterns:
//   - Arrange(piId, outcome) before each test
//   - Singleton lifetime in WebApplicationFactory.ConfigureTestServices
//   - Deterministic; no network
//
// Lives in CheckoutWebApplicationFactory test project (NOT production assembly).

using System.Collections.Concurrent;

namespace TravelAssistant.Checkout.Cancellation.Testing;

public sealed class FakePaymentProviderCancelClient : IPaymentProviderCancelClient
{
    private readonly ConcurrentDictionary<string, ProviderCancelResult> _arranged = new(StringComparer.Ordinal);

    public void Arrange(string providerPaymentIntentId, ProviderCancelResult result)
        => _arranged[providerPaymentIntentId] = result;

    public void Reset() => _arranged.Clear();

    public Task<ProviderCancelResult> CancelAsync(
        string providerPaymentIntentId,
        CancellationToken ct)
    {
        if (_arranged.TryGetValue(providerPaymentIntentId, out var result))
            return Task.FromResult(result);

        // Default: void-accepted (most common pre-capture path within 60min window)
        return Task.FromResult(new ProviderCancelResult(
            Outcome: ProviderCancelOutcome.Accepted,
            CancelType: CancelType.Void,
            ProviderRefundId: null,
            FailureCode: null,
            ProviderRequestId: $"req_fake_{Guid.NewGuid():N}"));
    }
}
