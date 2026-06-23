using System.IO;
using System.Security.Claims;
using System.Text.Json;

namespace TravelAssistant.Api.Checkout;

// Hotfix for BUG-1 (WI-1) and BUG-2 (WI-2): body-hashed idempotency and
// status-code-preserving replay. Drop-in replacement for the /confirm handler
// in CheckoutEndpoints.cs. Other endpoints unchanged.
public static class CheckoutEndpointsHotfix
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapCheckoutConfirmEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/checkout/confirm", async (
            HttpContext http,
            ICheckoutSessionStore store,
            IPaymentProvider payments,
            IIdempotencyStore idem,
            CancellationToken ct) =>
        {
            if (!http.Request.Headers.TryGetValue(IdempotencyHeader, out var idemKeyValues)
                || string.IsNullOrWhiteSpace(idemKeyValues))
            {
                return Results.BadRequest(new { error = "idempotency_key_required" });
            }
            var idemKey = idemKeyValues.ToString();

            // Read body once, into a string we can hash AND deserialize.
            http.Request.EnableBuffering();
            string rawBody;
            using (var reader = new StreamReader(http.Request.Body, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync(ct);
                http.Request.Body.Position = 0;
            }

            var bodyHash = IIdempotencyStore.ComputeCanonicalBodyHash(rawBody);
            var subject = http.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? http.User?.FindFirstValue("sub");

            // WI-1: lookup by (key, bodyHash, subject).
            var lookup = idem.Lookup(idemKey, bodyHash, subject);
            switch (lookup.Kind)
            {
                case IdempotencyLookupKind.Hit:
                    // WI-2: replay original status + body verbatim.
                    return Results.Text(lookup.ResponseJson,
                        contentType: "application/json",
                        statusCode: lookup.StatusCode);

                case IdempotencyLookupKind.BodyMismatch:
                    // RFC draft-ietf-httpapi-idempotency-key-header.
                    return Results.Problem(
                        type: "https://travel-assistant.example.com/problems/idempotency-key-conflict",
                        title: "Idempotency-Key conflict",
                        detail: "An Idempotency-Key was reused with a different request body.",
                        statusCode: StatusCodes.Status422UnprocessableEntity);

                case IdempotencyLookupKind.InFlight:
                    return Results.Problem(
                        type: "https://travel-assistant.example.com/problems/idempotency-in-progress",
                        title: "Request already in progress",
                        detail: "A request with this Idempotency-Key is currently being processed.",
                        statusCode: StatusCodes.Status409Conflict);
            }

            // Reserve so concurrent same-key requests get InFlight from now on.
            if (!idem.TryReserve(idemKey, bodyHash, subject))
            {
                return Results.Problem(
                    type: "https://travel-assistant.example.com/problems/idempotency-in-progress",
                    title: "Request already in progress",
                    statusCode: StatusCodes.Status409Conflict);
            }

            try
            {
                var req = JsonSerializer.Deserialize<ConfirmRequest>(rawBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (req is null || string.IsNullOrWhiteSpace(req.SessionId))
                    return SaveAndReturn(StatusCodes.Status400BadRequest, new { error = "session_id_required" });

                var order = store.Get(req.SessionId);
                if (order is null)
                    return SaveAndReturn(StatusCodes.Status404NotFound, new { error = "session_not_found" });

                if (order.Step != CheckoutStep.Payment)
                    return SaveAndReturn(StatusCodes.Status409Conflict,
                        new { error = "invalid_step", current = order.Step.ToString() });

                if (string.IsNullOrWhiteSpace(req.PaymentToken))
                    return SaveAndReturn(StatusCodes.Status400BadRequest, new { error = "payment_token_required" });

                var intent = await payments.ChargeAsync(req.PaymentToken,
                    order.SubtotalCents, order.Currency, idemKey, ct);

                var nextStep = intent.Status == "succeeded"
                    ? CheckoutStep.Confirmation
                    : CheckoutStep.Failed;
                order = store.Update(order with { Payment = intent, Step = nextStep });

                var resp = new ConfirmResponse(order.OrderId, intent.Status, intent.AmountCents);
                var status = intent.Status == "succeeded"
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status402PaymentRequired;
                return SaveAndReturn(status, resp);
            }
            catch
            {
                idem.ReleaseReservation(idemKey, subject);
                throw;
            }

            IResult SaveAndReturn(int status, object payload)
            {
                var json = JsonSerializer.Serialize(payload);
                idem.Save(idemKey, bodyHash, subject, status, json);
                return Results.Text(json, "application/json", statusCode: status);
            }
        });

        return app;
    }
}
