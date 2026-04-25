using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Inputs for creating a tie-on station on a Well.
///
/// <para>
/// <b>Required:</b> the three observed values (depth, inclination,
/// azimuth) — these come from the rig and anchor every downstream
/// trajectory calculation.
/// </para>
///
/// <para>
/// <b>Optional grid coordinates</b> (North / East / Northing / Easting /
/// VerticalReference / SubSeaReference / VerticalSectionDirection):
/// derived values normally set during data import or by an upstream
/// system. Default to 0 when omitted; the user can fill them later via
/// the edit page if a particular reference frame is in use.
/// </para>
/// </summary>
public sealed record CreateTieOnDto(
    [Required(ErrorMessage = "Depth is required.")]
    double Depth,

    [Required(ErrorMessage = "Inclination is required.")]
    [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
    double Inclination,

    [Required(ErrorMessage = "Azimuth is required.")]
    [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
    double Azimuth,

    double North = 0,
    double East = 0,
    double Northing = 0,
    double Easting = 0,
    double VerticalReference = 0,
    double SubSeaReference = 0,
    double VerticalSectionDirection = 0);
