namespace SDI.Enki.Shared.Wells.Formations;

/// <summary>
/// Lightweight row for the formations grid. Ordered by
/// <see cref="FromVertical"/> on the list endpoint so the layout
/// reads top-down.
/// </summary>
public sealed record FormationSummaryDto(
    int Id,
    int WellId,
    string Name,
    double FromVertical,
    double ToVertical,
    double Resistance,
    string? RowVersion);
