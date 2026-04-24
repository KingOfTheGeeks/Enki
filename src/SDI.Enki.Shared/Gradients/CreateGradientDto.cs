namespace SDI.Enki.Shared.Gradients;

public sealed record CreateGradientDto(
    string Name,
    int Order,
    int? ParentId = null,
    DateTime? Timestamp = null,
    double? Voltage = null,
    double? Frequency = null,
    int? Frame = null);
