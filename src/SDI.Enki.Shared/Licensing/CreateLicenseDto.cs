using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Licensing;

/// <summary>
/// Request body for <c>POST /licenses</c>. The operator picks the
/// <c>LicenseKey</c> in the wizard (clicks Generate or types one);
/// the customer needs that GUID to activate the license, so the
/// server doesn't auto-generate it. <c>IssuedAt</c> is set server-side.
/// </summary>
public sealed record CreateLicenseDto(
    [Required, MaxLength(200)]
    string Licensee,

    /// <summary>
    /// Customer-typed activation key. Generated client-side via the
    /// wizard's "Generate Key" button (<c>Guid.NewGuid()</c>) or pasted
    /// from an external source. Cannot be <see cref="Guid.Empty"/>.
    /// </summary>
    [Required]
    Guid LicenseKey,

    [Required]
    DateTime ExpiresAt,

    /// <summary>Tool ids (master DB Guid) to bake in.</summary>
    [Required, MinLength(1)]
    IReadOnlyList<Guid> ToolIds,

    /// <summary>
    /// Per-tool calibration choice. Key = ToolId, Value = chosen
    /// calibration id (any of that tool's calibrations, current or
    /// historical). Tools listed in <see cref="ToolIds"/> with no
    /// entry here default to that tool's current calibration.
    /// </summary>
    IReadOnlyDictionary<Guid, Guid>? CalibrationOverridesByToolId,

    [Required]
    LicenseFeaturesDto Features);
