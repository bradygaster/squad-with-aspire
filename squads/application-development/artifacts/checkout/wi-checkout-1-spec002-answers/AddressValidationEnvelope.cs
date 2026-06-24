// DR-CO-007 — Address validation envelope for POST /api/checkout/details/validate.
// Mirrors CancelErrorEnvelope (DR-CANCEL-005, commit 3e8df6b) shape exactly:
//   - Codes.All (frozen IReadOnlyList<string>) — single source of truth for QA enumeration guard
//   - Codes.ForEnum(AddressValidationCode) — total switch projection, throws on unmapped enum (loud-fail-on-drift)
//   - ^Code* const fields on Codes static class — reflection-friendly drift sentinel
//   - Fields.All — same shape for the 8-field allowlist
// Ownership: app-dev OWNS; QA CONSUMES via `using static`; review-deployment ASSERTS on deployed surface.
// Zero hand-rolled ToSnakeCase in tests/Checkout/ by construction.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TravelAssistant.Checkout.Contracts;

/// <summary>
/// Allowlist taxonomy for POST /api/checkout/details/validate 422 responses.
/// Provider-native codes (Loqate / SmartyStreets / Google) are mapped server-side
/// via IAddressValidationProvider; unmapped provider codes emit
/// checkout.address_validation.unmapped_code telemetry and project to a generic
/// fieldError, never raw provider strings to the client.
/// </summary>
public enum AddressValidationCode
{
    PostalCodeInvalid,
    PostalCodeMismatchCountry,
    StreetNotFound,
    CityNotFound,
    CountryUnsupported,
    TravelerNameInvalid,
    PhoneInvalid,
    EmailInvalid,
}

/// <summary>
/// Field discriminator on the 422 envelope — 8-value allowlist matching the
/// 8 testid fields specified by experience-design (e0c1bed §4.1).
/// </summary>
public enum AddressValidationField
{
    Street1,
    Street2,
    City,
    Region,
    PostalCode,
    Country,
    Phone,
    Email,
}

public static class AddressValidationEnvelope
{
    /// <summary>
    /// 422 response envelope shape. ConfirmRequest body is unchanged from
    /// CheckoutErrorEnvelope — flat siblings, no nesting beyond what's required.
    /// </summary>
    public sealed record FieldError(string Field, string Code);
    public sealed record ValidateResponse(bool Valid, IReadOnlyList<FieldError>? FieldErrors);

    public static class Codes
    {
        public const string PostalCodeInvalid          = "POSTAL_CODE_INVALID";
        public const string PostalCodeMismatchCountry  = "POSTAL_CODE_MISMATCH_COUNTRY";
        public const string StreetNotFound             = "STREET_NOT_FOUND";
        public const string CityNotFound               = "CITY_NOT_FOUND";
        public const string CountryUnsupported         = "COUNTRY_UNSUPPORTED";
        public const string TravelerNameInvalid        = "TRAVELER_NAME_INVALID";
        public const string PhoneInvalid               = "PHONE_INVALID";
        public const string EmailInvalid               = "EMAIL_INVALID";

        public static readonly IReadOnlyList<string> All = ImmutableArray.Create(
            PostalCodeInvalid,
            PostalCodeMismatchCountry,
            StreetNotFound,
            CityNotFound,
            CountryUnsupported,
            TravelerNameInvalid,
            PhoneInvalid,
            EmailInvalid);

        /// <summary>
        /// Total function — throws on unmapped enum values. Loud-fail-on-drift:
        /// adding an enum value without updating this switch breaks the build.
        /// </summary>
        public static string ForEnum(AddressValidationCode code) => code switch
        {
            AddressValidationCode.PostalCodeInvalid         => PostalCodeInvalid,
            AddressValidationCode.PostalCodeMismatchCountry => PostalCodeMismatchCountry,
            AddressValidationCode.StreetNotFound            => StreetNotFound,
            AddressValidationCode.CityNotFound              => CityNotFound,
            AddressValidationCode.CountryUnsupported        => CountryUnsupported,
            AddressValidationCode.TravelerNameInvalid       => TravelerNameInvalid,
            AddressValidationCode.PhoneInvalid              => PhoneInvalid,
            AddressValidationCode.EmailInvalid              => EmailInvalid,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unmapped AddressValidationCode — update AddressValidationEnvelope.Codes.ForEnum."),
        };
    }

    public static class Fields
    {
        public const string Street1    = "street1";
        public const string Street2    = "street2";
        public const string City       = "city";
        public const string Region     = "region";
        public const string PostalCode = "postalCode";
        public const string Country    = "country";
        public const string Phone      = "phone";
        public const string Email      = "email";

        public static readonly IReadOnlyList<string> All = ImmutableArray.Create(
            Street1, Street2, City, Region, PostalCode, Country, Phone, Email);

        public static string ForEnum(AddressValidationField field) => field switch
        {
            AddressValidationField.Street1    => Street1,
            AddressValidationField.Street2    => Street2,
            AddressValidationField.City       => City,
            AddressValidationField.Region     => Region,
            AddressValidationField.PostalCode => PostalCode,
            AddressValidationField.Country    => Country,
            AddressValidationField.Phone      => Phone,
            AddressValidationField.Email      => Email,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unmapped AddressValidationField — update AddressValidationEnvelope.Fields.ForEnum."),
        };
    }
}

/// <summary>
/// DR-CO-007 — Per-provider mapper seam mirroring IProviderReasonMapper from
/// DR-CANCEL-004. Each provider adapter (Loqate, SmartyStreets, Google) owns
/// its own normalization. NO cross-normalization — silent normalization masks
/// provider API drift. Unmapped provider code → null, caller emits
/// `checkout.address_validation.unmapped_code` telemetry and projects to a
/// generic envelope code per field semantics.
/// </summary>
public interface IAddressValidationProvider
{
    /// <summary>
    /// Maps a provider-native validation result onto the allowlist envelope.
    /// Returns null when the provider's code is not in this adapter's mapping
    /// table; caller is responsible for unmapped-code telemetry + fallback.
    /// </summary>
    AddressValidationCode? MapProviderCode(string providerCode, AddressValidationField field);

    /// <summary>
    /// Read-only view of this adapter's mapping table — for QA exhaustive-coverage
    /// tests without table duplication (mirrors Stripe/Adyen mapper.MappingTable
    /// pattern from wi-cancel-1-mappers commit 06873f7).
    /// </summary>
    IReadOnlyDictionary<string, AddressValidationCode> MappingTable { get; }
}
