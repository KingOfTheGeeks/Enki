using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Inputs for updating a tie-on. Same shape as
/// <see cref="CreateTieOnDto"/> minus the create-time helpers (every
/// field is rewritten on PUT). Validation mirrors the create DTO.
/// </summary>
public sealed record UpdateTieOnDto(
    [Required(ErrorMessage = "Depth is required.")]
    double Depth,

    [Required(ErrorMessage = "Inclination is required.")]
    [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
    double Inclination,

    [Required(ErrorMessage = "Azimuth is required.")]
    [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
    double Azimuth,

    double North,
    double East,
    double Northing,
    double Easting,
    double VerticalReference,
    double SubSeaReference,
    double VerticalSectionDirection);
