using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Inputs for creating a single survey station. Computed fields
/// (<c>VerticalDepth</c>, <c>DoglegSeverity</c>, …) are not accepted
/// from the wire — they're populated by the Calculate endpoint after
/// the row exists.
/// </summary>
public sealed record CreateSurveyDto(
    [Required(ErrorMessage = "Depth is required.")]
    double Depth,

    [Required(ErrorMessage = "Inclination is required.")]
    [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
    double Inclination,

    [Required(ErrorMessage = "Azimuth is required.")]
    [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
    double Azimuth);
