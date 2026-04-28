using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Inputs for updating a tie-on. Same shape as
/// <see cref="CreateTieOnDto"/> minus the create-time helpers (every
/// field is rewritten on PUT). Validation mirrors the create DTO.
///
/// <para>
/// <see cref="RowVersion"/> is the optimistic-concurrency token: the
/// base64-encoded byte sequence the client last fetched. Stale value
/// 409s with a reload-and-retry message.
/// </para>
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
    double VerticalSectionDirection,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
