using AspireWithSquad.AuthApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AspireWithSquad Auth API",
        Version = "v1",
        Description = "Auth endpoints. Password policy: min 10 / max 128 chars, must contain at least 3 of " +
                      "{lowercase, uppercase, digit, symbol}, must not equal email local-part, no whitespace padding. " +
                      "Failure responses use code 'WEAK_PASSWORD' with a human-readable 'message'. " +
                      "Client-side strength meters are hint-only — the API is the source of truth."
    });
});

var requireVerify = builder.Configuration.GetValue("Auth:RequireEmailVerification", defaultValue: true);
var tokenLifetimeMinutes = builder.Configuration.GetValue("Auth:VerificationTokenLifetimeMinutes", defaultValue: 60);
var resendCooldown = builder.Configuration.GetValue("Auth:ResendCooldownSeconds", defaultValue: 60);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAuthService>(sp => new InMemoryAuthService(
    new InMemoryAuthService.AuthOptions(requireVerify, TimeSpan.FromMinutes(tokenLifetimeMinutes), resendCooldown),
    sp.GetRequiredService<TimeProvider>()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var auth = app.MapGroup("/api/auth");

auth.MapPost("/register", async (RegisterRequest req, IAuthService svc, CancellationToken ct) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrEmpty(req.Password))
        return Results.BadRequest(new ErrorResponse("INVALID_REQUEST", "email and password are required."));

    var (ok, reason) = PasswordPolicy.Validate(req.Password, req.Email);
    if (!ok)
        return Results.BadRequest(new ErrorResponse("WEAK_PASSWORD", reason));

    var outcome = await svc.RegisterAsync(req.Email, req.Password, req.DisplayName, ct);
    if (outcome.Status == RegisterStatus.EmailTaken)
        return Results.Conflict(new ErrorResponse("EMAIL_TAKEN", "An account with this email already exists."));

    var body = new RegisterResponse(
        Token: outcome.SessionToken,
        User: outcome.User!,
        RequiresVerification: outcome.RequiresVerification);
    return Results.Created($"/api/users/{outcome.User!.Id}", body);
})
.WithName("Register")
.Produces<RegisterResponse>(StatusCodes.Status201Created)
.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
.Produces<ErrorResponse>(StatusCodes.Status409Conflict);

auth.MapGet("/verify", async ([FromQuery] string? token, IAuthService svc, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(token))
        return Results.BadRequest(new ErrorResponse("TOKEN_INVALID"));

    var outcome = await svc.VerifyAsync(token, ct);
    return outcome.Status switch
    {
        VerifyStatus.Verified => Results.Ok(new VerifySuccessResponse(true, outcome.User!)),
        VerifyStatus.TokenExpired => Results.BadRequest(new ErrorResponse("TOKEN_EXPIRED")),
        VerifyStatus.TokenUsed => Results.Json(new ErrorResponse("TOKEN_USED"), statusCode: StatusCodes.Status410Gone),
        _ => Results.BadRequest(new ErrorResponse("TOKEN_INVALID"))
    };
})
.WithName("VerifyEmail")
.Produces<VerifySuccessResponse>(StatusCodes.Status200OK)
.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
.Produces<ErrorResponse>(StatusCodes.Status410Gone);

auth.MapPost("/verify/resend", async (ResendRequest req, IAuthService svc, CancellationToken ct) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new ErrorResponse("INVALID_REQUEST", "email is required."));

    var outcome = await svc.ResendAsync(req.Email, ct);
    return Results.Accepted(value: new ResendAcceptedResponse(outcome.CooldownSeconds));
})
.WithName("ResendVerification")
.Produces<ResendAcceptedResponse>(StatusCodes.Status202Accepted)
.Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
