namespace SDI.Enki.Shared.Tools;

/// <summary>
/// Stub list of tools for Run selection. Placeholder for the real
/// <c>AMR.Core.Tools</c> integration; keep short.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolStub> All { get; } = new[]
    {
        new ToolStub("MWD-A1", "CAL-MWD-A1"),
        new ToolStub("MWD-B1", "CAL-MWD-B1"),
        new ToolStub("LWD-A1", "CAL-LWD-A1"),
    };

    public static ToolStub? FindByName(string? name) =>
        name is null ? null : All.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record ToolStub(string Name, string CalibrationName);
