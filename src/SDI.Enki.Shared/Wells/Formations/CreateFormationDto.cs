using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.Formations;

/// <summary>
/// Inputs for creating a Formation top.
/// <c>FromMeasured &lt;= ToMeasured</c> is enforced by the controller
/// (returns 400 ValidationProblem if violated). The depth range must
/// also fall inside the well's Survey MD envelope — TVD is derived
/// from those Surveys via minimum-curvature interpolation, so values
/// outside the envelope cannot be resolved.
/// </summary>
public sealed record CreateFormationDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "From-measured depth is required.")]
    double FromMeasured,

    [Required(ErrorMessage = "To-measured depth is required.")]
    double ToMeasured,

    [Required(ErrorMessage = "Resistance is required.")]
    double Resistance,

    string? Description = null);
