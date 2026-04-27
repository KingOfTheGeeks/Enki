using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells;

/// <summary>
/// The well's canonical magnetic-reference triple — total field
/// strength, dip, declination — plus audit metadata. Returned by
/// <c>GET /tenants/{code}/jobs/{jobId}/wells/{wellId}/magnetics</c>.
///
/// <para>
/// Storage convention mirrors the legacy lookup: <c>BTotal</c> in
/// nT, <c>Dip</c> + <c>Declination</c> in signed degrees. The
/// rendering edge handles the unit suffix via
/// <c>EnkiQuantity.MagneticFluxDensity</c> for BTotal and bare
/// degrees for the angles.
/// </para>
/// </summary>
public sealed record MagneticsDto(
    int     Id,
    int     WellId,
    double  BTotal,
    double  Dip,
    double  Declination,
    DateTimeOffset  CreatedAt,
    string?         CreatedBy,
    DateTimeOffset? UpdatedAt,
    string?         UpdatedBy);

/// <summary>
/// Inputs for creating or updating a Well's magnetic reference.
/// PUT semantics — same payload whether the row exists or not;
/// the controller upserts. Bounds match the form-side validation
/// in <c>MagneticsEdit.razor</c> so a direct API call faces the
/// same physical envelope as a form submission.
/// </summary>
public sealed record SetMagneticsDto(
    [Required(ErrorMessage = "Total field is required.")]
    [Range(0d, 100_000d, ErrorMessage = "Total field must be between 0 and 100,000 nT.")]
    double BTotal,

    [Required(ErrorMessage = "Dip is required.")]
    [Range(-90d, 90d, ErrorMessage = "Dip must be between -90 and 90 degrees.")]
    double Dip,

    [Required(ErrorMessage = "Declination is required.")]
    [Range(-180d, 180d, ErrorMessage = "Declination must be between -180 and 180 degrees.")]
    double Declination);
