using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Inputs for updating a Run. Same shape as <see cref="CreateRunDto"/>
/// minus immutables (<c>Type</c> is fixed at create time — a
/// gradient run can't become a rotary run mid-flight) and plus the
/// optimistic-concurrency token <see cref="RowVersion"/>.
///
/// <para>
/// <see cref="ToolId"/> is settable on update. Assigning a tool to a
/// previously tool-less run triggers a calibration snapshot. Once
/// shots/logs exist, the tool can be CHANGED but not CLEARED — the
/// controller rejects a null <see cref="ToolId"/> when the run has
/// child captures, since clearing would orphan their snapshot
/// references.
/// </para>
///
/// <para>
/// Magnetics fields (BTotal / Dip / Declination) update the run's
/// Magnetics row in place. They share <see cref="RowVersion"/> with
/// the Run row — one form, one save, one concurrency check.
/// </para>
/// </summary>
public sealed record UpdateRunDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(100, ErrorMessage = "Name must be 100 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Description is required.")]
    [MaxLength(500, ErrorMessage = "Description must be 500 characters or fewer.")]
    string Description,

    [Required(ErrorMessage = "Start depth is required.")]
    [Range(0d, 100_000d, ErrorMessage = "Start depth must be between 0 and 100,000 metres.")]
    double StartDepth,

    [Required(ErrorMessage = "End depth is required.")]
    [Range(0d, 100_000d, ErrorMessage = "End depth must be between 0 and 100,000 metres.")]
    double EndDepth,

    [Required(ErrorMessage = "Magnetic total field (BTotal) is required.")]
    [Range(20_000d, 80_000d,
        ErrorMessage = "BTotal must be between 20,000 and 80,000 nT (typical Earth values).")]
    double BTotalNanoTesla,

    [Required(ErrorMessage = "Magnetic dip is required.")]
    [Range(-90d, 90d, ErrorMessage = "Dip must be between -90 and 90 degrees.")]
    double DipDegrees,

    [Required(ErrorMessage = "Magnetic declination is required.")]
    [Range(-180d, 180d, ErrorMessage = "Declination must be between -180 and 180 degrees.")]
    double DeclinationDegrees,

    [Range(0d, 100d, ErrorMessage = "Bridle length must be between 0 and 100 metres.")]
    double? BridleLength,           // Gradient-only

    [Range(0d, 1_000d, ErrorMessage = "Current injection must be between 0 and 1,000 amps.")]
    double? CurrentInjection,       // Gradient-only

    /// <summary>
    /// Optional soft FK to master <c>Tool.Id</c>. Null = "no tool";
    /// not allowed when the run already has shots/logs (controller-
    /// side guard).
    /// </summary>
    Guid? ToolId,

    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
