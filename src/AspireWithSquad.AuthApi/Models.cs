using System.Text.Json.Serialization;

namespace AspireWithSquad.AuthApi;

public sealed record RegisterRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("displayName")] string? DisplayName);

public sealed record ResendRequest(
    [property: JsonPropertyName("email")] string Email);

/// <summary>
/// POST /api/auth/register → 201. Token is issued IFF requiresVerification == false.
/// Drives client routing: true → /verify-email, false → /welcome.
/// </summary>
public sealed record RegisterResponse(
    [property: JsonPropertyName("token"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token,
    [property: JsonPropertyName("user")] UserDto User,
    [property: JsonPropertyName("requiresVerification")] bool RequiresVerification);

public sealed record UserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("emailVerified")] bool EmailVerified);

public sealed record VerifySuccessResponse(
    [property: JsonPropertyName("verified")] bool Verified,
    [property: JsonPropertyName("user")] UserDto User);

public sealed record ResendAcceptedResponse(
    [property: JsonPropertyName("cooldownSeconds")] int CooldownSeconds);

public sealed record ErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);
