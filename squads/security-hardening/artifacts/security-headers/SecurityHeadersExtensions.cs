// Drop-in security headers middleware for ASP.NET Core / Aspire services.
// Owner: security-hardening-squad. Integrate from ServiceDefaults or per-service Program.cs:
//
//     builder.Services.AddSecurityHeaders();
//     var app = builder.Build();
//     app.UseSecurityHeaders();
//
// Defaults are designed for API services (no inline scripts, deny framing).
// For Blazor / static UI hosts, pass a SecurityHeadersOptions with a relaxed CSP.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AspireWithSquad.Security;

public sealed class SecurityHeadersOptions
{
    /// <summary>Content-Security-Policy. API-safe default denies everything except self.</summary>
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    /// <summary>HSTS max-age in seconds. 1 year + preload-eligible.</summary>
    public int StrictTransportSecurityMaxAgeSeconds { get; set; } = 31_536_000;
    public bool StrictTransportSecurityIncludeSubdomains { get; set; } = true;
    public bool StrictTransportSecurityPreload { get; set; } = false;

    public string ReferrerPolicy { get; set; } = "no-referrer";
    public string XContentTypeOptions { get; set; } = "nosniff";
    public string XFrameOptions { get; set; } = "DENY";

    /// <summary>Permissions-Policy: disable powerful APIs by default.</summary>
    public string PermissionsPolicy { get; set; } =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    /// <summary>Cross-Origin isolation headers — safe defaults for APIs.</summary>
    public string CrossOriginOpenerPolicy { get; set; } = "same-origin";
    public string CrossOriginResourcePolicy { get; set; } = "same-site";
    public string CrossOriginEmbedderPolicy { get; set; } = "require-corp";

    /// <summary>Strip the Server header to reduce fingerprinting.</summary>
    public bool RemoveServerHeader { get; set; } = true;

    /// <summary>When false, HSTS is only emitted over HTTPS (recommended).</summary>
    public bool EmitHstsOverHttp { get; set; } = false;
}

public static class SecurityHeadersExtensions
{
    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        Action<SecurityHeadersOptions>? configure = null)
    {
        services.TryAddSingleton<IConfigureOptions<SecurityHeadersOptions>>(
            new ConfigureNamedOptions<SecurityHeadersOptions>(Options.DefaultName, o => configure?.Invoke(o)));
        services.AddOptions<SecurityHeadersOptions>();
        return services;
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetService<IOptions<SecurityHeadersOptions>>()?.Value
                      ?? new SecurityHeadersOptions();

        return app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;

                void Set(string name, string value)
                {
                    if (!string.IsNullOrEmpty(value) && !h.ContainsKey(name))
                    {
                        h[name] = value;
                    }
                }

                Set("Content-Security-Policy", options.ContentSecurityPolicy);
                Set("Referrer-Policy", options.ReferrerPolicy);
                Set("X-Content-Type-Options", options.XContentTypeOptions);
                Set("X-Frame-Options", options.XFrameOptions);
                Set("Permissions-Policy", options.PermissionsPolicy);
                Set("Cross-Origin-Opener-Policy", options.CrossOriginOpenerPolicy);
                Set("Cross-Origin-Resource-Policy", options.CrossOriginResourcePolicy);
                Set("Cross-Origin-Embedder-Policy", options.CrossOriginEmbedderPolicy);

                if (ctx.Request.IsHttps || options.EmitHstsOverHttp)
                {
                    var hsts = $"max-age={options.StrictTransportSecurityMaxAgeSeconds}";
                    if (options.StrictTransportSecurityIncludeSubdomains) hsts += "; includeSubDomains";
                    if (options.StrictTransportSecurityPreload) hsts += "; preload";
                    Set("Strict-Transport-Security", hsts);
                }

                if (options.RemoveServerHeader)
                {
                    h.Remove("Server");
                    h.Remove("X-Powered-By");
                }

                return Task.CompletedTask;
            });

            await next();
        });
    }
}
