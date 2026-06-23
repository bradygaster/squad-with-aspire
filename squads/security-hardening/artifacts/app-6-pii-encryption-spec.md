# APP-6 — PII Handling & Field-Level Encryption Spec

**Owner:** application-development-squad (implementer)
**Author:** security-hardening-squad (Bishop)
**Spec-version:** 1.0 — 2026-06-23
**Status:** Approved, ready to implement
**Companion docs:** `docs/security/sec-4/privacy.md` (in PR #39), this file

---

## 1. Why

The travel-assistant agent persists user trip context to Cosmos and Postgres. That context contains PII (passport names, dates of birth, emails, phone numbers, frequent-flyer numbers, free-text notes that frequently contain addresses). Storage-account encryption-at-rest is **not** sufficient — it does not protect against:

- accidental log/telemetry leakage,
- backup or replica exfiltration,
- read-only DB role compromise,
- application-tier insiders.

APP-6 introduces an explicit data-classification model and field-level encryption for the **Sensitive** tier.

---

## 2. Data classification (authoritative)

Every persisted field MUST be tagged with one of three classes. Default for new fields is **Sensitive** until reviewed.

| Class | Examples | Storage rule | LLM-context rule |
|---|---|---|---|
| **Public** | currency code, IATA code, country | plaintext | plaintext OK |
| **Internal** | itinerary IDs, day counts, cost totals | plaintext | plaintext OK |
| **Sensitive** | passenger names, DOB, email, phone, passport #, FF#, free-text notes | **encrypted at rest (this spec)** | **must pass through SEC-4 redactor first** |

Implementation: `[DataClass(DataClass.Sensitive)]` attribute on properties of any persisted DTO. EF Core / Cosmos serializer reads the attribute and routes through the encryption pipeline.

---

## 3. Encryption design

### 3.1 Key hierarchy

```
Customer-Managed Key (CMK) in Key Vault   ← rotated by ops (annual)
   └─ wraps Data Encryption Key (DEK)     ← per-tenant, cached in memory 1h
        └─ encrypts field bytes           ← AES-256-GCM
```

- CMK: RSA-2048 in the project's hardened Key Vault (SEC-1). Vault URI from `KeyVault:Uri` env var.
- DEK: 32-byte random, wrapped by CMK, persisted alongside ciphertext as `wrappedKey` (base64). One DEK per **tenant** (or "default" tenant for solo-user mode). Re-wrap on CMK rotation.
- Algorithm: **AES-256-GCM**. 12-byte nonce, 16-byte tag. No CBC, no ECB, no GCM-SIV.
- Envelope format (single string column):
  ```
  v1.<base64-wrappedKey>.<base64-nonce>.<base64-ciphertext+tag>
  ```
  Version prefix `v1.` lets us migrate algorithms without ambiguity.

### 3.2 Library

Use `Azure.Security.KeyVault.Keys.Cryptography` for wrap/unwrap and `System.Security.Cryptography.AesGcm` for the symmetric primitive. **Do not roll your own.**

### 3.3 Reference implementation (drop-in)

```csharp
// src/TravelAssistant.Security/PiiCipher.cs
public interface IPiiCipher {
    string Encrypt(string plaintext, string tenantId);
    string Decrypt(string envelope, string tenantId);
}

public sealed class PiiCipher : IPiiCipher {
    private readonly KeyClient _kv;
    private readonly string _cmkName;
    private readonly IMemoryCache _dekCache;

    public PiiCipher(KeyClient kv, IConfiguration cfg, IMemoryCache cache) {
        _kv = kv;
        _cmkName = cfg["Pii:CmkName"] ?? "pii-cmk";
        _dekCache = cache;
    }

    public string Encrypt(string plaintext, string tenantId) {
        var (dek, wrapped) = GetOrCreateDek(tenantId);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(dek, 16);
        aes.Encrypt(nonce, pt, ct, tag);
        return $"v1.{B64(wrapped)}.{B64(nonce)}.{B64(Concat(ct, tag))}";
    }

    public string Decrypt(string envelope, string tenantId) {
        var parts = envelope.Split('.');
        if (parts.Length != 4 || parts[0] != "v1")
            throw new CryptographicException("Unsupported envelope version");
        var wrapped = FromB64(parts[1]);
        var nonce   = FromB64(parts[2]);
        var ctTag   = FromB64(parts[3]);
        var ct  = ctTag.AsSpan(0, ctTag.Length - 16);
        var tag = ctTag.AsSpan(ctTag.Length - 16, 16);
        var dek = UnwrapDek(wrapped, tenantId);
        var pt  = new byte[ct.Length];
        using var aes = new AesGcm(dek, 16);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    // GetOrCreateDek / UnwrapDek: 1-hour MemoryCache on (tenantId, wrappedKeyHash).
    // Cold path: CryptographyClient.UnwrapKey(KeyWrapAlgorithm.RsaOaep256, wrapped).
}
```

### 3.4 EF Core / Cosmos integration

Two paths, pick the one matching the entity's persistence target:

**EF Core (Postgres):** `HasConversion<PiiEncryptedConverter>()` on each `[DataClass(Sensitive)]` string property. Converter calls `IPiiCipher` resolved from the DbContext's service provider.

**Cosmos:** `JsonConverter` registered via the Cosmos SDK serializer. Same `IPiiCipher` dependency. Property-level — do **not** encrypt whole documents (breaks indexing on Public/Internal fields).

---

## 4. Hard rules (Production Guard will check these)

ProductionGuard (SEC-5) will add three checks in the next iteration:

1. `IPiiCipher` is registered in DI (`services.AddSingleton<IPiiCipher, PiiCipher>()`).
2. `Pii:CmkName` resolves to a key that exists in the Key Vault at `KeyVault:Uri`.
3. No `[DataClass(Sensitive)]` property is mapped without the converter (reflection scan at startup, fail-fast).

Failure of any check → `/health/prod-guard` returns 503 → deploy gate fails.

---

## 5. Logging & telemetry rules (binding)

- **Never** log plaintext PII. Use `[LogProperty(Redact = true)]` on any DTO field carrying Sensitive data; structured logger drops the property. Free-text fields go through the SEC-4 PII regex redactor before logging.
- OpenTelemetry: explicitly **deny** Sensitive properties via the `ITracerProviderBuilder.AddProcessor(new PiiStrippingProcessor())` registered in `Program.cs`.
- Exception messages: stack traces are fine; do not include parameter values for methods that take Sensitive types.
- Aspire dashboard: `OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EVENT_LOG_ATTRIBUTES` MUST be **false** in non-Dev environments (ProductionGuard checks).

---

## 6. Key rotation

- CMK rotation: annual minimum, on-demand on suspicion. Rotation is a no-op for ciphertext — only re-wrap of cached DEKs is required. Pre-existing envelopes continue to decrypt with their stored `wrappedKey` until they're naturally read+rewritten.
- DEK rotation: passive. Each tenant gets a fresh DEK on first write per quarter (timestamp-stamped in DEK metadata). No bulk re-encrypt job — too expensive, not needed.
- Compromise scenario: rotate CMK, then run `dotnet run --project tools/ReencryptAll` to force-decrypt+re-encrypt every Sensitive column. Tool is part of this ticket.

---

## 7. Right-to-erasure (GDPR Art. 17)

Field-level encryption does **not** by itself satisfy erasure (ciphertext remains). Two-step:

1. Application deletes the row / document via the standard delete path.
2. On tenant deletion, **revoke the tenant's DEK** in Key Vault (delete the wrapped-key record). Any orphaned ciphertext referencing that DEK becomes permanently undecryptable — cryptographic erasure.

Document the procedure in `docs/security/sec-4/erasure-runbook.md` (separate ticket).

---

## 8. Test contract (QA will assert)

```csharp
// tests/TravelAssistant.Tests.Unit/Security/PiiCipherTests.cs
[Fact] void Encrypt_RoundTrips()                       // basic
[Fact] void Encrypt_TwoCallsProduceDifferentEnvelopes() // nonce uniqueness
[Fact] void Decrypt_WithWrongTenant_Throws()           // DEK isolation
[Fact] void Decrypt_TamperedCiphertext_Throws()        // GCM tag verifies
[Fact] void Envelope_V1Format_StableAcrossCalls()      // schema lock
[Fact] void Persisted_SensitiveField_IsNotPlaintextOnDisk() // EF/Cosmos integration
```

Last test is the most important — uses a real Postgres/Cosmos emulator in CI, asserts that querying the underlying storage directly returns the `v1.` envelope, never the cleartext.

---

## 9. Out of scope (do NOT bundle)

- Searchable encryption / deterministic encryption — adds attack surface, not needed for our query patterns.
- Hardware HSM beyond Key Vault Premium — Standard tier is sufficient for the threat model.
- Encryption of Public/Internal fields — wastes CPU, breaks indexing.
- Client-side encryption in the Next.js app — server is the encryption boundary.

---

## 10. Acceptance criteria

- [ ] `IPiiCipher` + `PiiCipher` implemented and DI-registered.
- [ ] `[DataClass]` attribute applied to every property on `TravelAssistant.Agent.Abstractions` DTOs that carries Sensitive data (Bishop will review the diff).
- [ ] EF Core `PiiEncryptedConverter` wired to all Sensitive Postgres columns.
- [ ] Cosmos `PiiEncryptedJsonConverter` wired to all Sensitive document properties.
- [ ] All 6 unit tests pass.
- [ ] ProductionGuard adds the 3 checks in §4; `/health/prod-guard` returns 503 if any fail.
- [ ] `tools/ReencryptAll` console app builds and round-trips on the in-memory provider profile.
- [ ] No new `error CS` warnings under `TreatWarningsAsErrors=true`.

---

## 11. Sequencing

This spec depends on:
- **SEC-1** Key Vault hardening — ✅ shipped (PR #39, INF-2 wire-in patch).
- **APP-2/APP-4** contract surface — ✅ shipped (PR #41, commit 9a10482) — gives us the DTOs to tag.
- **SEC-5** ProductionGuard endpoint — ✅ shipped (PR #39) — gives us the gate to extend.

This spec blocks:
- Any production deployment that persists trip plans for real users.
- QA-3 (privacy assertion suite).

---

— Bishop, security-hardening-squad
