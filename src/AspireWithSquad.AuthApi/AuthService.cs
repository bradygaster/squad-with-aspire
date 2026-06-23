using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AspireWithSquad.AuthApi;

public interface IAuthService
{
    Task<RegisterOutcome> RegisterAsync(string email, string password, string? displayName, CancellationToken ct);
    Task<VerifyOutcome> VerifyAsync(string token, CancellationToken ct);
    Task<ResendOutcome> ResendAsync(string email, CancellationToken ct);
}

public enum RegisterStatus { Created, EmailTaken }
public enum VerifyStatus { Verified, TokenInvalid, TokenExpired, TokenUsed }
public enum ResendStatus { Accepted, NoSuchEmail, AlreadyVerified }

public sealed record RegisterOutcome(RegisterStatus Status, UserDto? User, string? VerificationToken, string? SessionToken, bool RequiresVerification);
public sealed record VerifyOutcome(VerifyStatus Status, UserDto? User);
public sealed record ResendOutcome(ResendStatus Status, int CooldownSeconds);

public sealed class InMemoryAuthService : IAuthService
{
    public sealed record AuthOptions(bool RequireEmailVerification, TimeSpan TokenLifetime, int ResendCooldownSeconds);

    private sealed record UserRecord(string Id, string Email, string? DisplayName, string PasswordHash, bool EmailVerified);
    private sealed record TokenRecord(string Email, DateTimeOffset ExpiresAt, bool Used);

    private readonly ConcurrentDictionary<string, UserRecord> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TokenRecord> _tokens = new(StringComparer.Ordinal);
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public InMemoryAuthService(AuthOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public Task<RegisterOutcome> RegisterAsync(string email, string password, string? displayName, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = new UserRecord(
            Id: Guid.NewGuid().ToString("N"),
            Email: normalized,
            DisplayName: displayName,
            PasswordHash: HashPassword(password),
            EmailVerified: !_options.RequireEmailVerification);

        if (!_users.TryAdd(normalized, user))
            return Task.FromResult(new RegisterOutcome(RegisterStatus.EmailTaken, null, null, null, false));

        var dto = new UserDto(user.Id, user.Email, user.DisplayName, user.EmailVerified);

        if (_options.RequireEmailVerification)
        {
            _ = IssueToken(normalized);
            return Task.FromResult(new RegisterOutcome(RegisterStatus.Created, dto, null, null, true));
        }

        var session = NewOpaqueToken();
        return Task.FromResult(new RegisterOutcome(RegisterStatus.Created, dto, null, session, false));
    }

    public Task<VerifyOutcome> VerifyAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !_tokens.TryGetValue(token, out var rec))
            return Task.FromResult(new VerifyOutcome(VerifyStatus.TokenInvalid, null));
        if (rec.Used)
            return Task.FromResult(new VerifyOutcome(VerifyStatus.TokenUsed, null));
        if (rec.ExpiresAt <= _clock.GetUtcNow())
            return Task.FromResult(new VerifyOutcome(VerifyStatus.TokenExpired, null));
        if (!_users.TryGetValue(rec.Email, out var user))
            return Task.FromResult(new VerifyOutcome(VerifyStatus.TokenInvalid, null));

        _tokens[token] = rec with { Used = true };
        var verified = user with { EmailVerified = true };
        _users[rec.Email] = verified;
        return Task.FromResult(new VerifyOutcome(
            VerifyStatus.Verified,
            new UserDto(verified.Id, verified.Email, verified.DisplayName, true)));
    }

    public Task<ResendOutcome> ResendAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        // Enumeration-safe: always 202 with cooldown. Hard limits live in AuthRateLimitMiddleware.
        if (_users.TryGetValue(normalized, out var user) && !user.EmailVerified)
            _ = IssueToken(normalized);
        return Task.FromResult(new ResendOutcome(ResendStatus.Accepted, _options.ResendCooldownSeconds));
    }

    public string? PeekLatestTokenForTest(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return _tokens
            .Where(kv => string.Equals(kv.Value.Email, normalized, StringComparison.Ordinal) && !kv.Value.Used)
            .OrderByDescending(kv => kv.Value.ExpiresAt)
            .Select(kv => kv.Key)
            .FirstOrDefault();
    }

    public void ExpireAllTokensForTest()
    {
        var past = _clock.GetUtcNow().AddSeconds(-1);
        foreach (var key in _tokens.Keys.ToArray())
            if (_tokens.TryGetValue(key, out var v))
                _tokens[key] = v with { ExpiresAt = past };
    }

    private string IssueToken(string email)
    {
        var token = NewOpaqueToken();
        _tokens[token] = new TokenRecord(email, _clock.GetUtcNow().Add(_options.TokenLifetime), Used: false);
        return token;
    }

    private static string NewOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashPassword(string password)
    {
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations: 100_000, HashAlgorithmName.SHA256, outputLength: 32);
        var combined = new byte[salt.Length + hash.Length];
        salt.CopyTo(combined);
        hash.CopyTo(combined.AsSpan(salt.Length));
        return Convert.ToBase64String(combined);
    }
}
