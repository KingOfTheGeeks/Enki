namespace SDI.Enki.Shared.Tools;

/// <summary>
/// Display-name conventions for Tools. Mirrors Nabu's
/// <c>ToolMetadata.DisplayName</c>: "{Generation}-{last 3 digits of serial}",
/// e.g. "G2-093". Lives here in Shared so both API projections and Blazor
/// pages produce the same string for the same tool.
/// </summary>
public static class ToolDisplay
{
    public static string Name(string generation, int serialNumber) =>
        $"{generation}-{serialNumber % 1000:D3}";
}
