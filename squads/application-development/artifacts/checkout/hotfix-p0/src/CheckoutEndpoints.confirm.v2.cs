// WI-1a — /checkout/confirm wired to SEC-CHK-007 R1/R2/R3 + A1 + T13.
// Companion to IdempotencyStore.v2.cs. Drop-in replacement for prior CheckoutEndpoints.confirm.cs.

using System.Net.Mime;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TravelAssistant.Api.Checkout.Security;

namespace TravelAssistant.Api.Checkout;

public static partial class CheckoutEndpoints
{
    // /checkout/confirm TTL matches inventory hold (15 min) per SEC-CHK-007 A1.
    // /checkout/session uses 24h — caller passes its own TTL.
    private static readonly TimeSpan ConfirmTtl = TimeSpan.FromMinutes(15);

    public static void MapCheckoutConfirm(
        this IEndpointRouteBuilder routes,
        IIdempotencyStore store,
        IIdempotencyKeyDeriver deriver,
        ICheckoutConfirmHandler handler)
    {
        routes.MapPost("/api/checkout/confirm", async (HttpContext http) =>
        {
            // --- 1. Idempotency-Key header (mandatory) ----------------------------
            if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var idemHeader)
                || string.IsNullOrWhiteSpace(idemHeader))
            {
                return Results.Problem(
                    type: "https://travel-assistant.dev/problems/idempotency-key-required",
                    title: "Idempotency-Key header is required for /checkout/confirm",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            var idempotencyKey = idemHeader.ToString();

            // --- 2. Scope derivation (R2) ----------------------------------------
            string scope;
            try
            {
                var guestSessionId = http.Request.Cookies["ta_guest_session"];
                scope = IdempotencyKeyDerivation.BuildScope(http.User, guestSessionId);
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(
                    type: "https://travel-assistant.dev/problems/identity-required",
                    title: "Either an authenticated session or a guest session cookie is required",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var derivedKey = deriver.Derive(scope, idempotencyKey);

            // --- 3. Read body + canonicalize (R3) --------------------------------
            http.Request.EnableBuffering();
            string rawBody;
            using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
                http.Request.Body.Position = 0;
            }

            byte[] canonicalUtf8;
            try
            {
                canonicalUtf8 = JsonCanonicalizer.CanonicalizeUtf8(rawBody);
            }
            catch (JsonException)
            {
                return Results.Problem(
                    type: "https://travel-assistant.dev/problems/malformed-json",
                    title: "Request body is not valid JSON",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var bodyHash = IdempotencyKeyDerivation.HashBody(canonicalUtf8);

            // --- 4. Lookup -------------------------------------------------------
            var lookup = store.Lookup(derivedKey, bodyHash);
            switch (lookup.Kind)
            {
                case IdempotencyLookupKind.Hit:
                    // WI-2: replay original status verbatim.
                    return Results.Content(lookup.ResponseJson, MediaTypeNames.Application.Json, statusCode: lookup.StatusCode);

                case IdempotencyLookupKind.BodyMismatch:
                    return Results.Problem(
                        type: "https://travel-assistant.dev/problems/idempotency-key-conflict",
                        title: "Idempotency-Key reused with a different request body",
                        statusCode: StatusCodes.Status422UnprocessableEntity);

                case IdempotencyLookupKind.InFlight:
                    return Results.Problem(
                        type: "https://travel-assistant.dev/problems/idempotency-key-in-flight",
                        title: "Another request with this Idempotency-Key is currently being processed",
                        statusCode: StatusCodes.Status409Conflict);
            }

            // --- 5. Reserve (T13 caps) -------------------------------------------
            var clientIp = http.Connection.RemoteIpAddress?.ToString();
            var reservation = store.TryReserve(derivedKey, bodyHash, scope, clientIp, ConfirmTtl);

            switch (reservation.Outcome)
            {
                case ReservationOutcome.AlreadyInFlight:
                    return Results.Problem(
                        type: "https://travel-assistant.dev/problems/idempotency-key-in-flight",
                        title: "Another request with this Idempotency-Key is currently being processed",
                        statusCode: StatusCodes.Status409Conflict);

                case ReservationOutcome.SubjectCapExceeded:
                case ReservationOutcome.IpCapExceeded:
                    http.Response.Headers["Retry-After"] = "60";
                    return Results.Problem(
                        type: "https://travel-assistant.dev/problems/idempotency-cap-exceeded",
                        title: "Too many in-flight idempotency keys; retry after the configured window",
                        statusCode: StatusCodes.Status429TooManyRequests);
            }

            // --- 6. Execute + save -----------------------------------------------
            try
            {
                var result = await handler.HandleAsync(rawBody, http.RequestAborted);
                store.Save(derivedKey, bodyHash, result.StatusCode, result.ResponseJson, ConfirmTtl);
                return Results.Content(result.ResponseJson, MediaTypeNames.Application.Json, statusCode: result.StatusCode);
            }
            catch
            {
                store.ReleaseReservation(derivedKey);
                throw;
            }
        });
    }
}

public interface ICheckoutConfirmHandler
{
    Task<CheckoutConfirmResult> HandleAsync(string rawJsonBody, CancellationToken ct);
}

public sealed record CheckoutConfirmResult(int StatusCode, string ResponseJson);
