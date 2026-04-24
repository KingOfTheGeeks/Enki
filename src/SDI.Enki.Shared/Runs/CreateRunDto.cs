namespace SDI.Enki.Shared.Runs;

public sealed record CreateRunDto(
    string Name,
    string Description,
    string Type,                       // "Gradient" | "Rotary" | "Passive"
    double StartDepth,
    double EndDepth,
    double? BridleLength = null,       // Gradient-only
    double? CurrentInjection = null,   // Gradient-only
    DateTimeOffset? StartTimestamp = null,
    DateTimeOffset? EndTimestamp = null);
