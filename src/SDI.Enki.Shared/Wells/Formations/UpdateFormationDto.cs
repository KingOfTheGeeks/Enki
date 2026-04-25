using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.Formations;

public sealed record UpdateFormationDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "From-vertical depth is required.")]
    double FromVertical,

    [Required(ErrorMessage = "To-vertical depth is required.")]
    double ToVertical,

    [Required(ErrorMessage = "Resistance is required.")]
    double Resistance,

    string? Description);
