namespace SDI.Enki.Shared.Surveys;

public sealed record SurveyCalculationResponseDto(
    int WellId,
    int SurveysProcessed,
    int Precision,
    DateTimeOffset CalculatedAt);
