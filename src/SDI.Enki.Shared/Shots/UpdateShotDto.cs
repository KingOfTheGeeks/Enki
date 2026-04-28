using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Shots;

/// <summary>
/// Inputs for updating a Shot's identity columns. Binary / config /
/// result fields are managed via dedicated endpoints
/// (<c>POST /shots/{id}/binary</c>, <c>PUT /shots/{id}/config</c>,
/// etc.) rather than the bulk PUT — those touch large payloads
/// and need their own validation surface.
///
/// <para>
/// Carries the optimistic-concurrency token <see cref="RowVersion"/>;
/// stale-token saves return 409 ConflictProblem.
/// </para>
/// </summary>
public sealed record UpdateShotDto(
    [Required(ErrorMessage = "Shot name is required.")]
    [MaxLength(200, ErrorMessage = "Shot name must be 200 characters or fewer.")]
    string ShotName,

    [Required(ErrorMessage = "File time is required.")]
    DateTimeOffset FileTime,

    int? CalibrationId,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
