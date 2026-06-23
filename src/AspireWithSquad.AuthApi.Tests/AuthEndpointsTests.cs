using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspireWithSquad.AuthApi.Tests;

public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:RequireEmailVerification", "true");
            b.UseSetting("Auth:VerificationTokenLifetimeMinutes", "60");
            b.UseSetting("Auth:ResendCooldownSeconds", "60");
        });
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static InMemoryAuthService GetSvc(WebApplicationFactory<Program> f)
        => (InMemoryAuthService)f.Services.GetRequiredService<IAuthService>();

    [Fact]
    public async Task Register_RequiresVerification_ReturnsNoTokenAndFlagTrue()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("alice@example.com", "Sup3rSecret!42", "Alice"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>(Json);
        Assert.NotNull(body);
        Assert.True(body!.RequiresVerification);
        Assert.Null(body.Token);
        Assert.Equal("alice@example.com", body.User.Email);
        Assert.False(body.User.EmailVerified);
    }

    [Fact]
    public async Task Register_NoVerification_IssuesTokenAndFlagFalse()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
            b.UseSetting("Auth:RequireEmailVerification", "false"));
        using var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("bob@example.com", "Sup3rSecret!42", "Bob"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>(Json);
        Assert.False(body!.RequiresVerification);
        Assert.False(string.IsNullOrEmpty(body.Token));
        Assert.True(body.User.EmailVerified);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400WithWeakPasswordCode()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("carol@example.com", "short", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("WEAK_PASSWORD", err!.Code);
        Assert.False(string.IsNullOrEmpty(err.Message));
    }

    [Fact]
    public async Task Register_PasswordEqualsEmailLocalPart_RejectedAsWeak()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("MyLongLogin@example.com", "MyLongLogin", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("WEAK_PASSWORD", err!.Code);
    }

    [Fact]
    public async Task Register_Duplicate_Returns409EmailTaken()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("dup@example.com", "Sup3rSecret!42", null));
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("dup@example.com", "Sup3rSecret!42", null));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("EMAIL_TAKEN", err!.Code);
    }

    [Fact]
    public async Task Verify_HappyPath_Returns200WithVerifiedTrue()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("ed@example.com", "Sup3rSecret!42", null));

        var token = GetSvc(_factory).PeekLatestTokenForTest("ed@example.com");
        Assert.False(string.IsNullOrEmpty(token));

        var resp = await client.GetAsync($"/api/auth/verify?token={Uri.EscapeDataString(token!)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<VerifySuccessResponse>(Json);
        Assert.True(body!.Verified);
        Assert.True(body.User.EmailVerified);
    }

    [Fact]
    public async Task Verify_InvalidToken_Returns400TokenInvalid()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/auth/verify?token=not-a-real-token");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("TOKEN_INVALID", err!.Code);
    }

    [Fact]
    public async Task Verify_MissingToken_Returns400TokenInvalid()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/auth/verify");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("TOKEN_INVALID", err!.Code);
    }

    [Fact]
    public async Task Verify_ReusedToken_Returns410TokenUsed()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("frank@example.com", "Sup3rSecret!42", null));
        var token = GetSvc(_factory).PeekLatestTokenForTest("frank@example.com");
        await client.GetAsync($"/api/auth/verify?token={Uri.EscapeDataString(token!)}");

        var resp = await client.GetAsync($"/api/auth/verify?token={Uri.EscapeDataString(token!)}");
        Assert.Equal(HttpStatusCode.Gone, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("TOKEN_USED", err!.Code);
    }

    [Fact]
    public async Task Verify_ExpiredToken_Returns400TokenExpired()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("gary@example.com", "Sup3rSecret!42", null));

        var svc = GetSvc(_factory);
        var token = svc.PeekLatestTokenForTest("gary@example.com");
        svc.ExpireAllTokensForTest();

        var resp = await client.GetAsync($"/api/auth/verify?token={Uri.EscapeDataString(token!)}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(Json);
        Assert.Equal("TOKEN_EXPIRED", err!.Code);
    }

    [Fact]
    public async Task Resend_AlwaysReturns202WithCooldown_EvenForUnknownEmail()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/verify/resend",
            new ResendRequest("nobody@example.com"));

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ResendAcceptedResponse>(Json);
        Assert.Equal(60, body!.CooldownSeconds);
    }

    [Fact]
    public async Task Resend_KnownUnverifiedEmail_IssuesNewToken()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("hank@example.com", "Sup3rSecret!42", null));
        var svc = GetSvc(_factory);
        var firstToken = svc.PeekLatestTokenForTest("hank@example.com");

        var resp = await client.PostAsJsonAsync("/api/auth/verify/resend",
            new ResendRequest("hank@example.com"));

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var secondToken = svc.PeekLatestTokenForTest("hank@example.com");
        Assert.NotNull(firstToken);
        Assert.NotNull(secondToken);
        Assert.NotEqual(firstToken, secondToken);
    }
}
