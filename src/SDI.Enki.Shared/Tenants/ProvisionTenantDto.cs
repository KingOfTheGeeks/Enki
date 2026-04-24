using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tenants;

/// <summary>
/// Inputs for provisioning a new tenant. Annotated so direct-API callers
/// (anyone not going through the Blazor form) still get 400-level
/// validation from <c>[ApiController]</c>'s automatic ModelState check.
/// Rules mirror the canonical <c>DatabaseNaming.ValidateCode</c> regex
/// in Infrastructure so client and server agree on what a valid Code is.
///
/// Region deliberately omitted — it lives on Jobs, where the actual work
/// has a location. A tenant is a corporation and may operate globally.
///
/// Attributes are applied directly to the record's positional parameters
/// (no <c>[property: ...]</c> target). ASP.NET Core's validator for
/// record-shaped models reads parameter metadata.
/// </summary>
public sealed record ProvisionTenantDto(
    [Required(ErrorMessage = "Code is required.")]
    [RegularExpression(@"^[A-Z][A-Z0-9_]{0,23}$",
        ErrorMessage = "Code must be 1–24 chars, uppercase A–Z / digits / underscore, starting with a letter.")]
    string Code,

    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200)]
    string Name,

    [MaxLength(200)] string? DisplayName = null,
    [MaxLength(256)]
    [EmailAddress(ErrorMessage = "Not a valid email address.")]
    string? ContactEmail = null,
    string? Notes = null);
