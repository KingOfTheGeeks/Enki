using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Inputs for updating a single survey station. Same observed-field
/// surface as <see cref="CreateSurveyDto"/>; computed values are
/// owned by the auto-calc and overwritten on the next run.
///
/// <para>
/// <see cref="RowVersion"/> is the optimistic-concurrency token: the
/// base64-encoded byte sequence the client last fetched for this
/// row. The server applies it as a <c>WHERE rowversion = @v</c>
/// constraint on the UPDATE; a stale value surfaces as 409 Conflict
/// with a reload-and-retry message.
/// </para>
/// </summary>
public sealed record UpdateSurveyDto(
    [Required(ErrorMessage = "Depth is required.")]
    double Depth,

    [Required(ErrorMessage = "Inclination is required.")]
    [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
    double Inclination,

    [Required(ErrorMessage = "Azimuth is required.")]
    [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
    double Azimuth,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
