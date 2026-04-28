namespace SDI.Enki.Shared.Wells.Formations;

public sealed record FormationDetailDto(
    int Id,
    int WellId,
    string Name,
    string? Description,
    double FromVertical,
    double ToVertical,
    double Resistance,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
