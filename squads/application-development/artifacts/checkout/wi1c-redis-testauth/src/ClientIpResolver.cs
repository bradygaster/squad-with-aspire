// File: src/TravelAssistant.Api/Checkout/Idempotency/ClientIpResolver.cs
// Purpose: WI-1c — honor X-Forwarded-For for per-IP entry-cap evaluation (QA Case 8).
//
// SECURITY MODEL:
//   - X-Forwarded-For is ONLY honored when the immediate caller is a trusted proxy
//     (configured allowlist of CIDRs, e.g. Front Door / Container Apps ingress).
//   - When trusted, we take the LEFT-MOST entry — that is the original client per RFC 7239
//     contract assumed by Azure Front Door and Container Apps.
//   - When NOT trusted (e.g. direct hit in dev), we fall back to HttpContext.Connection.RemoteIpAddress.
//   - Malformed XFF → fall back, never throw.
//
// Anti-spoofing: a client can put anything in XFF. The only thing that makes XFF
// trustworthy is verifying the *immediate hop* (RemoteIpAddress) is a known reverse proxy.

using System.Net;

namespace TravelAssistant.Api.Checkout.Idempotency;

public sealed class ClientIpResolverOptions
{
    /// <summary>CIDR blocks of trusted reverse proxies. Empty = never trust XFF.</summary>
    public List<string> TrustedProxyCidrs { get; init; } = new();
}

public interface IClientIpResolver
{
    string Resolve(HttpContext ctx);
}

public sealed class ClientIpResolver : IClientIpResolver
{
    private readonly IReadOnlyList<(IPAddress prefix, int bits)> _trusted;

    public ClientIpResolver(ClientIpResolverOptions opts)
    {
        var parsed = new List<(IPAddress, int)>();
        foreach (var cidr in opts.TrustedProxyCidrs)
        {
            if (TryParseCidr(cidr, out var p, out var b)) parsed.Add((p, b));
        }
        _trusted = parsed;
    }

    public string Resolve(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null) return "unknown";

        if (IsTrustedProxy(remote) &&
            ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var xff) &&
            !string.IsNullOrWhiteSpace(xff))
        {
            // Left-most entry is the original client.
            var leftMost = xff.ToString().Split(',', 2, StringSplitOptions.TrimEntries)[0];
            // Strip optional port (IPv4 only — IPv6 must be bracketed if it has a port).
            var colon = leftMost.IndexOf(':');
            var ipPart = (colon > 0 && leftMost.Count(c => c == ':') == 1) ? leftMost[..colon] : leftMost;
            if (IPAddress.TryParse(ipPart, out var clientIp)) return clientIp.ToString();
        }

        return remote.ToString();
    }

    private bool IsTrustedProxy(IPAddress remote)
    {
        foreach (var (prefix, bits) in _trusted)
        {
            if (prefix.AddressFamily != remote.AddressFamily) continue;
            if (InSubnet(remote, prefix, bits)) return true;
        }
        return false;
    }

    private static bool TryParseCidr(string cidr, out IPAddress prefix, out int bits)
    {
        prefix = IPAddress.None; bits = 0;
        var parts = cidr.Split('/', 2);
        if (parts.Length != 2) return false;
        if (!IPAddress.TryParse(parts[0], out prefix!)) return false;
        if (!int.TryParse(parts[1], out bits)) return false;
        var max = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return bits >= 0 && bits <= max;
    }

    private static bool InSubnet(IPAddress addr, IPAddress prefix, int bits)
    {
        var a = addr.GetAddressBytes();
        var p = prefix.GetAddressBytes();
        if (a.Length != p.Length) return false;
        int fullBytes = bits / 8;
        int remainder = bits % 8;
        for (int i = 0; i < fullBytes; i++) if (a[i] != p[i]) return false;
        if (remainder == 0) return true;
        int mask = 0xFF << (8 - remainder) & 0xFF;
        return (a[fullBytes] & mask) == (p[fullBytes] & mask);
    }
}
