using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AspireWithSquad.RateLimiting;

/// <summary>
/// HMAC-SHA256 derivation of IP and account keys per spec §4.
/// </summary>
/// <remarks>
/// SECURITY: never accept caller-supplied raw IPs or emails into Redis without HMACing first.
/// The HMAC key (<see cref="RateLimitOptions.HmacKey"/>) must be rotated like any other server-side secret.
/// </remarks>
public sealed class RateLimitKeyDerivation
{
    private readonly byte[] _hmacKey;

    public RateLimitKeyDerivation(RateLimitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HmacKey))
            throw new InvalidOperationException("RateLimit:HmacKey is required.");
        _hmacKey = Convert.FromBase64String(options.HmacKey);
        if (_hmacKey.Length < 32)
            throw new InvalidOperationException("RateLimit:HmacKey must be at least 32 bytes (base64).");
    }

    /// <summary>HMAC-SHA256(key, lowercase(trim(email))). §4.2.</summary>
    public string AccountKey(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Hash(normalized);
    }

    /// <summary>
    /// HMAC-SHA256(key, normalized-ip). IPv4 = full; IPv6 = /64 prefix. §4.1.
    /// </summary>
    public string IpKey(IPAddress address)
    {
        string normalized;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            // /64 prefix: keep first 8 bytes, zero the rest.
            for (var i = 8; i < bytes.Length; i++) bytes[i] = 0;
            normalized = new IPAddress(bytes).ToString();
        }
        else
        {
            normalized = address.ToString();
        }
        return Hash(normalized);
    }

    private string Hash(string input)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(digest);
    }
}

/// <summary>
/// Resolves the caller IP from HttpContext, honoring X-Forwarded-For leftmost only when
/// <see cref="RateLimitOptions.TrustForwardedFor"/> is true (i.e., we are behind a trusted proxy).
/// </summary>
public static class ClientIpResolver
{
    public static IPAddress Resolve(Microsoft.AspNetCore.Http.HttpContext context, bool trustForwardedFor)
    {
        if (trustForwardedFor &&
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) &&
            xff.Count > 0)
        {
            // Leftmost untrusted hop per RFC 7239 §5.2.
            var first = xff[0]!.Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out var parsed))
                return parsed;
        }
        return context.Connection.RemoteIpAddress ?? IPAddress.None;
    }
}
