using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class JsonExpandableRow : ComponentBase
{
    [Parameter] public string? OldValues { get; set; }
    [Parameter] public string? NewValues { get; set; }

    private bool _expanded;

    private bool HasContent =>
        !string.IsNullOrWhiteSpace(OldValues) || !string.IsNullOrWhiteSpace(NewValues);

    private void Toggle() => _expanded = !_expanded;

    /// <summary>
    /// Best-effort indent the JSON string. If the source isn't valid
    /// JSON (audit row was written by a hand-rolled writer or the
    /// shape changed), fall back to the raw text — better than showing
    /// nothing and the audit row is still useful flat.
    /// </summary>
    private static string PrettyPrint(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(
                doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }
}
