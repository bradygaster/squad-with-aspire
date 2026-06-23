// CheckoutWebApplicationFactory.cs
// Drop into tests/TravelAssistant.Api.Tests/Checkout/
//
// One-time WebApplicationFactory<Program> customization for the checkout integration
// suite. Wires:
//   - FakePaymentProvider as IPaymentProvider (replaces Stripe-style real provider)
//   - "Test" auth scheme (Authorization: Bearer test:{sub})  — requires
//     ASPNETCORE_ENABLE_TEST_AUTH=1 to be set in the test host environment, which
//     this factory does in-process before host build.
//   - In-memory IdempotencyStore (default; Redis variant covered by separate fixture)
//
// Usage from a test class:
//
//   public class FailedPaymentReplayIntegrationTests
//       : IClassFixture<CheckoutWebApplicationFactory>
//   {
//       private readonly CheckoutWebApplicationFactory _factory;
//       public FailedPaymentReplayIntegrationTests(CheckoutWebApplicationFactory f) => _factory = f;
//
//       [Fact]
//       public async Task Declined_replay_preserves_402()
//       {
//           var client = _factory.CreateClient();
//           client.DefaultRequestHeaders.Authorization =
//               new AuthenticationHeaderValue("Bearer", "test:user-42");
//           // ... POST /checkout/confirm twice with same Idempotency-Key + tok_declined
//       }
//   }

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelAssistant.Api.Checkout;
using TravelAssistant.Api.Tests.Checkout.Fakes;

namespace TravelAssistant.Api.Tests.Checkout;

public sealed class CheckoutWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENABLE_TEST_AUTH", "1");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkout:IdempotencyBackend"]                    = "memory",
                ["Checkout:Idempotency:MaxEntriesPerSubject"]      = "1000",
                ["Checkout:Idempotency:MaxEntriesPerIp"]           = "5000",
                ["Checkout:Idempotency:ConfirmTtlMinutes"]         = "15",
                ["Checkout:Idempotency:SessionTtlHours"]           = "24",
                ["Checkout:Webhooks:TimestampToleranceSeconds"]    = "300",
                ["Checkout:Webhooks:SigningSecret"]                = "whsec_test_only_do_not_use_in_prod",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace real payment provider with deterministic fake.
            var existing = services.Where(d => d.ServiceType == typeof(IPaymentProvider)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddSingleton<IPaymentProvider, FakePaymentProvider>();

            // WebhookDispatchCounter only registered in test host (Development + test-auth gate
            // already satisfied above) so /webhooks/payments/_debug/event-count/{id} is live.
            services.AddSingleton<WebhookDispatchCounter>();
        });
    }
}
