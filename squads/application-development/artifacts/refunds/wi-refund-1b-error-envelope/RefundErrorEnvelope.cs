// WI-REFUND-1b — Error envelope alignment with experience-design UX spec (commit 4c84355).
// Spec freezes wire format: { "error": { "code": "REFUND_*", "message": "..." } }
// v1 shipped: { "error": "refund_already_exists", ... } — flat lowercase. Frontend won't parse.
//
// This patch:
//   1. Wraps every refunds 4xx/5xx response in { error: { code, message } } envelope.
//   2. Uppercases codes per spec §5.2 + state machine: REFUND_ALREADY_EXISTS,
//      REFUND_INELIGIBLE_{CANCELED|ALREADY_REFUNDED|NOT_CONFIRMED|WINDOW_EXPIRED},
//      IDEMPOTENCY_KEY_REQUIRED, IDEMPOTENCY_BODY_MISMATCH, REQUEST_IN_FLIGHT, RATE_LIMITED,
//      MALFORMED_JSON, ORDER_ID_REQUIRED.
//   3. Preserves existing 200/202 success bodies (RefundResponse record) — no change to happy path.
//   4. Preserves cached error responses in IdempotencyStore (re-cached in new shape on first call;
//      already-cached lowercase entries TTL out within 15min, no migration needed).
//
// PROVIDER_DECLINED / PROVIDER_TIMEOUT / PROVIDER_UNAVAILABLE / INSUFFICIENT_PROVIDER_FUNDS
// (from RefundWebhookHandler.cs) already match spec — no change needed to webhook handler.
//
// Apply: replace error-emission blocks in RefundsEndpoints.cs with calls to RefundError.* helpers.
// Drop-in: this file is a static helper; RefundsEndpoints.cs gets a one-line `using` + call-site swap.

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace TravelAssistant.Refunds;

internal static class RefundError
{
    public const string IdempotencyKeyRequired   = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyBodyMismatch  = "IDEMPOTENCY_BODY_MISMATCH";
    public const string RequestInFlight          = "REQUEST_IN_FLIGHT";
    public const string MalformedJson            = "MALFORMED_JSON";
    public const string OrderIdRequired          = "ORDER_ID_REQUIRED";
    public const string RateLimited              = "RATE_LIMITED";
    public const string Unauthorized             = "UNAUTHORIZED";
    public const string RefundAlreadyExists      = "REFUND_ALREADY_EXISTS";

    // Eligibility codes — REFUND_INELIGIBLE_{REASON}. Most-specific-wins precedence preserved
    // (canceled > already_refunded > not_confirmed > window_expired).
    public static string Ineligible(string reason) => reason.ToUpperInvariant() switch
    {
        "CANCELED"         => "REFUND_INELIGIBLE_CANCELED",
        "ALREADY_REFUNDED" => "REFUND_INELIGIBLE_ALREADY_REFUNDED",
        "NOT_CONFIRMED"    => "REFUND_INELIGIBLE_NOT_CONFIRMED",
        "WINDOW_EXPIRED"   => "REFUND_INELIGIBLE_WINDOW_EXPIRED",
        _                  => "REFUND_INELIGIBLE_UNKNOWN" // safety net — should never fire
    };

    /// <summary>
    /// Serializes the spec-frozen wire envelope: { "error": { "code", "message" } }.
    /// Additional fields (e.g. refundId for REFUND_ALREADY_EXISTS) live alongside `error`,
    /// not nested inside it — per spec §5.2 ("server returns machine codes in error.code").
    /// </summary>
    public static string Envelope(string code, string message, object? extra = null)
    {
        if (extra is null)
            return JsonSerializer.Serialize(new { error = new { code, message } });

        // Merge envelope + extra fields at root. JsonSerializer doesn't merge anonymous types,
        // so we build a dictionary. Keys: error (object), plus extras (e.g. refundId, status).
        var root = new Dictionary<string, object?>
        {
            ["error"] = new { code, message }
        };
        foreach (var prop in extra.GetType().GetProperties())
            root[prop.Name] = prop.GetValue(extra);
        return JsonSerializer.Serialize(root);
    }

    /// <summary>Convenience: write envelope to IResult with given status code.</summary>
    public static IResult Result(int statusCode, string code, string message, object? extra = null)
        => Results.Content(Envelope(code, message, extra), "application/json", statusCode);
}

// =============================================================================
// CALL-SITE REPLACEMENTS for RefundsEndpoints.cs
// =============================================================================
// Each block below replaces the corresponding v1 emission. Apply as surgical edits
// (search/replace on the old line). Cached-in-store bodies use Envelope() so replay
// returns the new shape byte-for-byte.
//
// --- 1. Idempotency-Key missing ---
// OLD: return Results.Problem(statusCode: 400, title: "Idempotency-Key header required");
// NEW: return RefundError.Result(400, RefundError.IdempotencyKeyRequired,
//          "Idempotency-Key header is required for POST /api/refunds");
//
// --- 2. Unauthorized (no sub claim) ---
// OLD: return Results.Unauthorized();
// NEW: return RefundError.Result(401, RefundError.Unauthorized, "Authentication required");
//
// --- 3. Malformed JSON ---
// OLD: return Results.Problem(statusCode: 400, title: "Malformed JSON");
// NEW: return RefundError.Result(400, RefundError.MalformedJson, "Request body is not valid JSON");
//
// --- 4. orderId required ---
// OLD: return Results.Problem(statusCode: 400, title: "orderId required");
// NEW: return RefundError.Result(400, RefundError.OrderIdRequired, "orderId is required in request body");
//
// --- 5. Rate limited ---
// OLD: return Results.StatusCode(429);
// NEW: return RefundError.Result(429, RefundError.RateLimited,
//          "Refund request limit exceeded. Try again later.");
//
// --- 6. Idempotency body mismatch (422) ---
// OLD: return Results.Problem(statusCode: 422, title: "Idempotency-Key reused with different body");
// NEW: return RefundError.Result(422, RefundError.IdempotencyBodyMismatch,
//          "Idempotency-Key was reused with a different request body");
//
// --- 7. Request in flight (409) ---
// OLD: return Results.Problem(statusCode: 409, title: "Request in flight");
// NEW: return RefundError.Result(409, RefundError.RequestInFlight,
//          "Another refund request with this Idempotency-Key is in flight");
//
// --- 8. IDOR-safe 404 (order missing OR sub mismatch) ---
// OLD: return Results.NotFound();
// NEW: return RefundError.Result(404, "ORDER_NOT_FOUND", "Order not found");
//   NOTE: spec doesn't enumerate ORDER_NOT_FOUND in §5.2 (it's not user-facing copy — 404 
//   shouldn't reach the modal). Frontend maps 404 via generic handler. Code added for log clarity.
//
// --- 9. Order not refundable (409 + eligibility reason) ---
// OLD: var resp = JsonSerializer.Serialize(new { error = "order_not_refundable", reason = ineligible });
//      await idem.PutAsync(cacheKey, canonical, 409, resp, TimeSpan.FromMinutes(15), ct);
//      return Results.Content(resp, "application/json", statusCode: 409);
// NEW: var code = RefundError.Ineligible(ineligible);
//      var resp = RefundError.Envelope(code, EligibilityMessage(code));
//      await idem.PutAsync(cacheKey, canonical, 409, resp, TimeSpan.FromMinutes(15), ct);
//      return Results.Content(resp, "application/json", statusCode: 409);
//
// --- 10. Refund already exists (409 + existing refund pointer) ---
// OLD: var resp = JsonSerializer.Serialize(new { error = "refund_already_exists", refundId = existing.RefundId, status = existing.Status });
//      await idem.PutAsync(cacheKey, canonical, 409, resp, TimeSpan.FromMinutes(15), ct);
//      return Results.Content(resp, "application/json", statusCode: 409);
// NEW: var resp = RefundError.Envelope(
//          RefundError.RefundAlreadyExists,
//          "A refund for this order already exists",
//          new { refundId = existing.RefundId, status = existing.Status });
//      await idem.PutAsync(cacheKey, canonical, 409, resp, TimeSpan.FromMinutes(15), ct);
//      return Results.Content(resp, "application/json", statusCode: 409);
//
// Plain-language eligibility messages — server-side fallback only. Frontend owns user copy
// via the §5.2 mapping table; these messages exist for non-modal consumers (CLI, support tools).
internal static class EligibilityMessages
{
    public static string EligibilityMessage(string code) => code switch
    {
        "REFUND_INELIGIBLE_CANCELED"         => "Order was canceled and cannot be refunded",
        "REFUND_INELIGIBLE_ALREADY_REFUNDED" => "This order has already been refunded",
        "REFUND_INELIGIBLE_NOT_CONFIRMED"    => "Order is not in a refundable state",
        "REFUND_INELIGIBLE_WINDOW_EXPIRED"   => "The 24-hour refund window has expired",
        _                                    => "Order is not refundable"
    };
}
