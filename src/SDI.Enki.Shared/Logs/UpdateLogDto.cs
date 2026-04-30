using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Logs;

/// <summary>
/// Inputs for updating a Log's identity columns. Binary + config
/// + result files are managed via dedicated endpoints; this DTO
/// covers ShotName / FileTime / CalibrationId only. Carries the
/// optimistic-concurrency token <see cref="RowVersion"/>.
/// </summary>
public sealed record UpdateLogDto(
    [Required(ErrorMessage = "Shot name is required.")]
    [MaxLength(200, ErrorMessage = "Shot name must be 200 characters or fewer.")]
    string ShotName,

    [Required(ErrorMessage = "File time is required.")]
    DateTimeOffset FileTime,

    int? CalibrationId,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
