# Security Headers Middleware

Drop-in ASP.NET Core middleware shipping a hardened default set of HTTP security
response headers (CSP, HSTS, COOP/COEP/CORP, Permissions-Policy, frame deny,
nosniff, no-referrer).

Owner: **security-hardening-squad**.

## Integrate

1. Copy `SecurityHeadersExtensions.cs` into your service (or, preferred, into a
   shared `ServiceDefaults` project so every Aspire service gets it).
2. In `Program.cs`:
   ```csharp
   builder.Services.AddSecurityHeaders();
   var app = builder.Build();
   app.UseSecurityHeaders();
   ```
3. For UI services (Blazor, MVC, SPA host), override CSP:
   ```csharp
   builder.Services.AddSecurityHeaders(o =>
   {
       o.ContentSecurityPolicy =
           "default-src 'self'; img-src 'self' data: https:; " +
           "script-src 'self'; style-src 'self' 'unsafe-inline'; " +
           "connect-src 'self' https://*.applicationinsights.azure.com; " +
           "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
       o.CrossOriginEmbedderPolicy = "unsafe-none";
   });
   ```

## What it sets

| Header | Default | Why |
|---|---|---|
| `Content-Security-Policy` | API-safe deny-all | Mitigate XSS / injection |
| `Strict-Transport-Security` | 1y + includeSubDomains (HTTPS only) | Force TLS |
| `X-Content-Type-Options` | `nosniff` | Block MIME sniffing |
| `X-Frame-Options` | `DENY` | Anti-clickjacking |
| `Referrer-Policy` | `no-referrer` | Don't leak URLs |
| `Permissions-Policy` | All powerful APIs off | Least-privilege |
| `Cross-Origin-*-Policy` | same-origin / same-site / require-corp | Cross-origin isolation |
| Removes | `Server`, `X-Powered-By` | Reduce fingerprinting |
