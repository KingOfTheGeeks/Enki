using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.CommonMeasures;

/// <summary>
/// Inputs for creating a common-measure depth-ranged scalar.
/// <c>FromMeasured &lt;= ToMeasured</c> enforced by the controller, plus
/// the range must fall inside the well's Survey MD envelope (TVD is
/// derived via minimum-curvature interpolation).
/// </summary>
public sealed record CreateCommonMeasureDto(
    [Required(ErrorMessage = "From-measured depth is required.")]
    double FromMeasured,

    [Required(ErrorMessage = "To-measured depth is required.")]
    double ToMeasured,

    [Required(ErrorMessage = "Value is required.")]
    double Value);
