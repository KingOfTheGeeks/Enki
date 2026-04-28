namespace SDI.Enki.Shared.Wells.CommonMeasures;

public sealed record CommonMeasureDetailDto(
    int Id,
    int WellId,
    double FromVertical,
    double ToVertical,
    double Value,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
