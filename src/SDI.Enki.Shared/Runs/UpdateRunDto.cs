using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Inputs for updating a Run. Same shape as <see cref="CreateRunDto"/>
/// minus immutables (<c>Type</c> is fixed at create time — a
/// gradient run can't become a rotary run mid-flight) and plus the
/// optimistic-concurrency token <see cref="RowVersion"/>.
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

    [Range(0d, 100d, ErrorMessage = "Bridle length must be between 0 and 100 metres.")]
    double? BridleLength,           // Gradient-only

    [Range(0d, 1_000d, ErrorMessage = "Current injection must be between 0 and 1,000 amps.")]
    double? CurrentInjection,       // Gradient-only

    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
