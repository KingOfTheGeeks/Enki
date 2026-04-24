namespace SDI.Enki.Shared.Gradients;

public sealed record GradientDetailDto(
    int Id,
    string Name,
    int Order,
    bool IsValid,
    Guid RunId,
    int? ParentId,
    DateTime Timestamp,
    double? Voltage,
    double? Frequency,
    int? Frame,
    int ShotCount,
    int SolutionCount,
    int FileCount,
    int CommentCount);
