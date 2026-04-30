using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Inputs for creating a Run on a Job. <c>Type</c> is the
/// <c>RunType</c> SmartEnum name — Gradient / Rotary / Passive — the
/// controller resolves it and 400s on an unknown value; the
/// attribute below catches the empty/oversized cases.
///
/// <para>
/// <c>BridleLength</c> + <c>CurrentInjection</c> are Gradient-only;
/// nullable so non-Gradient runs can omit them. Validation here
/// only enforces the universal shape — controller-side rules
/// (e.g. "BridleLength required when Type is Gradient") layer on
/// top.
/// </para>
///
/// <para>
/// <b>Tool / Magnetics</b> (issue #26 follow-up):
/// </para>
/// <list type="bullet">
///   <item><see cref="ToolId"/> is OPTIONAL — operators can create a
///   run before the tool is selected. Shots / Logs cannot be added
///   until a tool is assigned (gated server-side); see
///   <c>ShotsController.Create</c> / <c>LogsController.Create</c>.
///   Assigning a tool triggers a calibration snapshot.</item>
///   <item><see cref="BTotalNanoTesla"/>, <see cref="DipDegrees"/>,
///   <see cref="DeclinationDegrees"/> are REQUIRED — every Run
///   carries its own Magnetics row, manually entered (no auto-prefill
///   from the well's canonical Magnetics).</item>
/// </list>
/// </summary>
public sealed record CreateRunDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(100, ErrorMessage = "Name must be 100 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Description is required.")]
    [MaxLength(500, ErrorMessage = "Description must be 500 characters or fewer.")]
    string Description,

    [Required(ErrorMessage = "Run type is required.")]
    string Type,                       // "Gradient" | "Rotary" | "Passive"

    [Required(ErrorMessage = "Start depth is required.")]
    [Range(0d, 100_000d, ErrorMessage = "Start depth must be between 0 and 100,000 metres.")]
    double StartDepth,

    [Required(ErrorMessage = "End depth is required.")]
    [Range(0d, 100_000d, ErrorMessage = "End depth must be between 0 and 100,000 metres.")]
    double EndDepth,

    /// <summary>
    /// Magnetic flux density (BTotal) in nanotesla. Required at
    /// creation — every Run carries its own Magnetics record.
    /// Typical onshore values are ~45,000–60,000 nT.
    /// </summary>
    [Required(ErrorMessage = "Magnetic total field (BTotal) is required.")]
    [Range(20_000d, 80_000d,
        ErrorMessage = "BTotal must be between 20,000 and 80,000 nT (typical Earth values).")]
    double BTotalNanoTesla,

    /// <summary>
    /// Magnetic dip in signed degrees (positive = downward in N
    /// hemisphere). Required at creation. Bounded to physical extremes.
    /// </summary>
    [Required(ErrorMessage = "Magnetic dip is required.")]
    [Range(-90d, 90d, ErrorMessage = "Dip must be between -90 and 90 degrees.")]
    double DipDegrees,

    /// <summary>
    /// Magnetic declination in signed degrees (positive = east of true
    /// north). Required at creation. Bounded to physical extremes.
    /// </summary>
    [Required(ErrorMessage = "Magnetic declination is required.")]
    [Range(-180d, 180d, ErrorMessage = "Declination must be between -180 and 180 degrees.")]
    double DeclinationDegrees,

    [Range(0d, 100d, ErrorMessage = "Bridle length must be between 0 and 100 metres.")]
    double? BridleLength = null,       // Gradient-only

    [Range(0d, 1_000d, ErrorMessage = "Current injection must be between 0 and 1,000 amps.")]
    double? CurrentInjection = null,   // Gradient-only

    /// <summary>
    /// Optional soft FK to master <c>Tool.Id</c>. Null = "no tool yet";
    /// shots/logs gated until assigned. Validated server-side against
    /// the master DB at controller entry.
    /// </summary>
    Guid? ToolId = null,

    DateTimeOffset? StartTimestamp = null,
    DateTimeOffset? EndTimestamp = null);
