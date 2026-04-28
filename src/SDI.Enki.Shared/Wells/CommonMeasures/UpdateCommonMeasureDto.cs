using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.CommonMeasures;

public sealed record UpdateCommonMeasureDto(
    [Required(ErrorMessage = "From-vertical depth is required.")]
    double FromVertical,

    [Required(ErrorMessage = "To-vertical depth is required.")]
    double ToVertical,

    [Required(ErrorMessage = "Value is required.")]
    double Value,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
