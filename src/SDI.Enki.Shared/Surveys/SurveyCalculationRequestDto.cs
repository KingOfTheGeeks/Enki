namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Request parameters for <c>POST /tenants/{code}/jobs/{jobId}/wells/{wellId}/surveys/calculate</c>.
/// Drives Marduk's <c>ISurveyCalculator.Process</c> against all Surveys for
/// the Well, using the Well's first TieOn (or the one specified).
/// </summary>
public sealed record SurveyCalculationRequestDto(
    int MetersToCalculateDegreesOver = 30,
    int Precision = 6,
    int? TieOnId = null);
