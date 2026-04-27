using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Request parameters for <c>POST /tenants/{code}/jobs/{jobId}/wells/{wellId}/surveys/calculate</c>.
/// Drives Marduk's <c>ISurveyCalculator.Process</c> against all Surveys for
/// the Well, using the Well's first TieOn (or the one specified).
/// </summary>
public sealed record SurveyCalculationRequestDto(
    [Range(1, 1_000, ErrorMessage = "Meters to calculate degrees over must be between 1 and 1,000.")]
    int MetersToCalculateDegreesOver = 30,

    [Range(0, 15, ErrorMessage = "Precision must be between 0 and 15 decimal places.")]
    int Precision = 6,

    int? TieOnId = null);
