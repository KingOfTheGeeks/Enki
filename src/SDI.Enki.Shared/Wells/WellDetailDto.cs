namespace SDI.Enki.Shared.Wells;

/// <summary>
/// Full Well projection for the detail page. Carries audit fields so
/// the UI can show "Created by … at …" / "Updated by … at …"; the
/// Blazor detail page renders these in the audit footer that mirrors
/// JobDetail.
/// </summary>
public sealed record WellDetailDto(
    int Id,
    string Name,
    string Type,
    int SurveyCount,
    int TieOnCount,
    int TubularCount,
    int FormationCount,
    int CommonMeasureCount,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
