using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Inputs for updating a single survey station. Same observed-field
/// surface as <see cref="CreateSurveyDto"/>; computed values are
/// owned by the Calculate endpoint and overwritten on the next run.
/// </summary>
public sealed record UpdateSurveyDto(
    [Required(ErrorMessage = "Depth is required.")]
    double Depth,

    [Required(ErrorMessage = "Inclination is required.")]
    [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
    double Inclination,

    [Required(ErrorMessage = "Azimuth is required.")]
    [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
    double Azimuth);
