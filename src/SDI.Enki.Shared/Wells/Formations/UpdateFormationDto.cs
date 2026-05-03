using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.Formations;

public sealed record UpdateFormationDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "From-measured depth is required.")]
    double FromMeasured,

    [Required(ErrorMessage = "To-measured depth is required.")]
    double ToMeasured,

    [Required(ErrorMessage = "Resistance is required.")]
    double Resistance,

    string? Description,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
