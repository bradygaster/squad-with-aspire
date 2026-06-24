// SPDX-License-Identifier: MIT
// WI-CHECKOUT-1 contracts v2 amendment — DR-CO-005 reconciliation per ideation Q-CO-4.
// Idempotency-Key contract aligned to refunds shape: per-cart scope + JCS body hash + 15min TTL.
// Supersedes DR-CO-005 (24h, per-session). Backwards-compatible: header name unchanged,
// error envelope codes unchanged (IDEMPOTENCY_KEY_CONFLICT, REQUEST_IN_FLIGHT, IDEMPOTENCY_KEY_REQUIRED).
namespace TravelAssistant.Checkout.Contracts;

/// <summary>
/// Idempotency contract for POST /api/checkout/{id}/confirm.
/// Header: Idempotency-Key (required).
/// Scope: per-cart (cartId stable across resume/refresh/back-button within session lifetime).
/// Cache key: H(sub:checkout:{cartId}:{clientGeneratedKey}) + JCS-canonicalized body hash.
/// TTL: 15 minutes (aligns with refunds; sufficient for retry-after-3DS-redirect path).
/// </summary>
public static class IdempotencyContract
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Min/max length in bytes. Same bounds as Stripe Idempotency-Key (UUIDv4 is 36 chars, comfortable).</summary>
    public const int MinLengthBytes = 16;
    public const int MaxLengthBytes = 128;

    /// <summary>Cache TTL in minutes. Aligns with refunds-v1 idempotency window.</summary>
    public const int TtlMinutes = 15;

    /// <summary>Scope discriminator for cache key. Per-cart (cartId-scoped), NOT per-user or per-session.</summary>
    public const string Scope = "per-cart";

    /// <summary>Cache key sub-prefix. Combined with cartId + client key + JCS body hash.</summary>
    public const string CacheKeySubPrefix = "sub:checkout";
}
