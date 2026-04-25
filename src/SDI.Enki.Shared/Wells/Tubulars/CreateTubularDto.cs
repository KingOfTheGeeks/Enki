using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.Tubulars;

/// <summary>
/// Inputs for creating a tubular segment on a Well. Type is the
/// <c>TubularType</c> SmartEnum name — Casing / Liner / Tubing /
/// DrillPipe / OpenHole. The controller resolves it and 400s on an
/// unknown value.
/// </summary>
public sealed record CreateTubularDto(
    [Required(ErrorMessage = "Tubular type is required.")]
    string Type,

    [Required(ErrorMessage = "Order is required.")]
    int Order,

    [Required(ErrorMessage = "From-measured depth is required.")]
    double FromMeasured,

    [Required(ErrorMessage = "To-measured depth is required.")]
    double ToMeasured,

    [Required(ErrorMessage = "Diameter is required.")]
    double Diameter,

    [Required(ErrorMessage = "Weight is required.")]
    double Weight,

    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string? Name = null);
