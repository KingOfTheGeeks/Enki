using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.CommonMeasures;

public sealed record UpdateCommonMeasureDto(
    [Required(ErrorMessage = "From-measured depth is required.")]
    double FromMeasured,

    [Required(ErrorMessage = "To-measured depth is required.")]
    double ToMeasured,

    [Required(ErrorMessage = "Value is required.")]
    double Value,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
