using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Logs;

/// <summary>
/// Inputs for creating a Log under a Run. Phase 2 reshape: lookup
/// FKs collapse to optional <c>CalibrationId</c>. Binary + config
/// land via dedicated endpoints (<c>POST /logs/{id}/binary</c>,
/// <c>PUT /logs/{id}/config</c>).
/// </summary>
public sealed record CreateLogDto(
    [Required(ErrorMessage = "Shot name is required.")]
    [MaxLength(200, ErrorMessage = "Shot name must be 200 characters or fewer.")]
    string ShotName,

    [Required(ErrorMessage = "File time is required.")]
    DateTimeOffset FileTime,

    int? CalibrationId = null);
