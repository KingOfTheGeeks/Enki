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
/// the controller upserts.
/// </summary>
public sealed record SetMagneticsDto(
    [Required] double BTotal,
    [Required] double Dip,
    [Required] double Declination);
