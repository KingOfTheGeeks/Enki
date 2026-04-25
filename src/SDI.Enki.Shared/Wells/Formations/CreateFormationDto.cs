using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.Formations;

/// <summary>
/// Inputs for creating a Formation top.
/// <c>FromVertical &lt;= ToVertical</c> is enforced by the controller
/// (returns 400 ValidationProblem if violated).
/// </summary>
public sealed record CreateFormationDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "From-vertical depth is required.")]
    double FromVertical,

    [Required(ErrorMessage = "To-vertical depth is required.")]
    double ToVertical,

    [Required(ErrorMessage = "Resistance is required.")]
    double Resistance,

    string? Description = null);
