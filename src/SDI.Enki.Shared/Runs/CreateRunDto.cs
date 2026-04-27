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

    [Range(0d, 100d, ErrorMessage = "Bridle length must be between 0 and 100 metres.")]
    double? BridleLength = null,       // Gradient-only

    [Range(0d, 1_000d, ErrorMessage = "Current injection must be between 0 and 1,000 amps.")]
    double? CurrentInjection = null,   // Gradient-only

    DateTimeOffset? StartTimestamp = null,
    DateTimeOffset? EndTimestamp = null);
