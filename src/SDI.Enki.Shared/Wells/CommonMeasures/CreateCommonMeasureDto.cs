using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.CommonMeasures;

/// <summary>
/// Inputs for creating a common-measure depth-ranged scalar.
/// <c>FromVertical &lt;= ToVertical</c> enforced by the controller.
/// </summary>
public sealed record CreateCommonMeasureDto(
    [Required(ErrorMessage = "From-vertical depth is required.")]
    double FromVertical,

    [Required(ErrorMessage = "To-vertical depth is required.")]
    double ToVertical,

    [Required(ErrorMessage = "Value is required.")]
    double Value);
