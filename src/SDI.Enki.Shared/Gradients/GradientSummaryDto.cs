namespace SDI.Enki.Shared.Gradients;

public sealed record GradientSummaryDto(
    int Id,
    string Name,
    int Order,
    bool IsValid,
    Guid RunId,
    int? ParentId,
    DateTime Timestamp,
    int ShotCount);
