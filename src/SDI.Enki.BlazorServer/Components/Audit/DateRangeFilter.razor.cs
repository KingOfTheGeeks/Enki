using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class DateRangeFilter : ComponentBase
{
    [Parameter] public DateTimeOffset? From { get; set; }
    [Parameter] public EventCallback<DateTimeOffset?> FromChanged { get; set; }

    [Parameter] public DateTimeOffset? To { get; set; }
    [Parameter] public EventCallback<DateTimeOffset?> ToChanged { get; set; }

    /// <summary>Fired after From + To both update via a preset, so the host can
    /// trigger a single refetch instead of two.</summary>
    [Parameter] public EventCallback OnRangeChanged { get; set; }

    private async Task OnFromChanged(ChangeEventArgs e)
    {
        From = ParseLocal((string?)e.Value);
        await FromChanged.InvokeAsync(From);
        await OnRangeChanged.InvokeAsync();
    }

    private async Task OnToChanged(ChangeEventArgs e)
    {
        To = ParseLocal((string?)e.Value);
        await ToChanged.InvokeAsync(To);
        await OnRangeChanged.InvokeAsync();
    }

    private async Task SetPreset(TimeSpan window)
    {
        var now = DateTimeOffset.Now;
        From = now - window;
        To   = now;
        await FromChanged.InvokeAsync(From);
        await ToChanged.InvokeAsync(To);
        await OnRangeChanged.InvokeAsync();
    }

    private async Task ClearRange()
    {
        From = null;
        To   = null;
        await FromChanged.InvokeAsync(From);
        await ToChanged.InvokeAsync(To);
        await OnRangeChanged.InvokeAsync();
    }

    private static string FormatLocal(DateTimeOffset? v) =>
        v is { } x ? x.LocalDateTime.ToString("yyyy-MM-ddTHH:mm") : "";

    private static DateTimeOffset? ParseLocal(string? raw) =>
        DateTime.TryParse(raw, out var dt)
            ? new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))
            : null;
}
