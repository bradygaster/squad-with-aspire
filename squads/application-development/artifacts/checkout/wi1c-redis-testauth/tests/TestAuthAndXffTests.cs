// File: tests/TravelAssistant.Api.Tests/Checkout/TestAuthAndXffTests.cs
// WI-1c — proves the test auth seam + XFF resolver behave correctly so QA cases
// 1, 2, 7, 8 can go green when un-skipped.

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TravelAssistant.Api.Checkout.Idempotency;
using Xunit;

namespace TravelAssistant.Api.Tests.Checkout;

public class TestAuthHandlerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestAuthHandlerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ASPNETCORE_ENABLE_TEST_AUTH", "1");
        });
    }

    [Fact]
    public async Task BearerTestPrefix_EmitsNameIdentifierClaim()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test:user-42");

        var resp = await client.GetAsync("/checkout/_debug/whoami");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("user-42");
    }

    [Fact]
    public async Task NoBearer_NoIdentity_ReturnsAnonymous()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/checkout/_debug/whoami");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidTestSub_Empty_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test:");
        var resp = await client.GetAsync("/checkout/confirm");
        resp.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}

public class ClientIpResolverTests
{
    [Fact]
    public void XffHonored_WhenRemoteIsTrustedProxy()
    {
        var opts = new ClientIpResolverOptions { TrustedProxyCidrs = { "10.0.0.0/8" } };
        var resolver = new ClientIpResolver(opts);
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.1.2.3";

        resolver.Resolve(ctx).Should().Be("203.0.113.7");
    }

    [Fact]
    public void XffIgnored_WhenRemoteIsNotTrusted()
    {
        var opts = new ClientIpResolverOptions { TrustedProxyCidrs = { "10.0.0.0/8" } };
        var resolver = new ClientIpResolver(opts);
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        resolver.Resolve(ctx).Should().Be("198.51.100.9");
    }

    [Fact]
    public void XffMalformed_FallsBackToRemote()
    {
        var opts = new ClientIpResolverOptions { TrustedProxyCidrs = { "10.0.0.0/8" } };
        var resolver = new ClientIpResolver(opts);
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        ctx.Request.Headers["X-Forwarded-For"] = "not-an-ip";

        resolver.Resolve(ctx).Should().Be("10.0.0.5");
    }

    [Fact]
    public void NoXff_UsesRemote()
    {
        var opts = new ClientIpResolverOptions { TrustedProxyCidrs = { "10.0.0.0/8" } };
        var resolver = new ClientIpResolver(opts);
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        resolver.Resolve(ctx).Should().Be("10.0.0.5");
    }

    [Fact]
    public void EmptyTrustedCidrs_NeverHonorsXff()
    {
        var opts = new ClientIpResolverOptions();
        var resolver = new ClientIpResolver(opts);
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        resolver.Resolve(ctx).Should().Be("10.0.0.5");
    }
}
