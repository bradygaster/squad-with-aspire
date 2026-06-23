// File: src/TravelAssistant.Api/Checkout/Auth/TestAuthHandler.cs
// Purpose: WI-1c — dev/test auth seam so QA merge-gate tests (Cases 1, 2, 7) can
// assert that derived cache keys are scoped to authenticated `sub`.
//
// Usage (Program.cs, ONLY when env is Development OR ASPNETCORE_ENABLE_TEST_AUTH=1):
//
//   if (builder.Environment.IsDevelopment() ||
//       Environment.GetEnvironmentVariable("ASPNETCORE_ENABLE_TEST_AUTH") == "1")
//   {
//       builder.Services
//           .AddAuthentication(TestAuthHandler.SchemeName)
//           .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
//               TestAuthHandler.SchemeName, _ => { });
//   }
//
// Client sends:  Authorization: Bearer test:<sub>
// Handler emits ClaimsPrincipal with ClaimTypes.NameIdentifier = <sub>.
//
// HARD GATE: Refuses to register in Production unless explicitly opted-in via env var.
// Even with opt-in, NEVER trust this in real auth flows — it accepts any sub claim.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TravelAssistant.Api.Checkout.Auth;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestBearer";
    private const string Prefix = "Bearer test:";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var header = headerValues.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var sub = header.Substring(Prefix.Length).Trim();
        if (string.IsNullOrEmpty(sub) || sub.Length > 200)
            return Task.FromResult(AuthenticateResult.Fail("Invalid test sub claim"));

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub),
                new Claim("sub", sub),
                new Claim("scope", "checkout:write")
            },
            authenticationType: SchemeName);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
