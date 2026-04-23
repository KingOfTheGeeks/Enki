namespace SDI.Enki.Shared.Wells;

public sealed record WellDetailDto(
    int Id,
    string Name,
    string Type,
    int SurveyCount,
    int TieOnCount,
    int TubularCount,
    int FormationCount,
    int CommonMeasureCount);
