using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class UserActivityTile : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Parameter, EditorRequired] public string IdentityId { get; set; } = "";
    [Parameter, EditorRequired] public string UserName   { get; set; } = "";

    /// <summary>Per-source row count. Defaults to 10; merged total is at most 2× this.</summary>
    [Parameter] public int Take { get; set; } = 10;

    private List<TimelineEntry>? _entries;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiIdentity");

        var identityTask = client.GetAsync<PagedResult<AuditLogEntryDto>>(
            $"admin/audit/identity/ApplicationUser/{Uri.EscapeDataString(IdentityId)}?take={Take}");
        var authTask = client.GetAsync<PagedResult<AuthEventEntryDto>>(
            $"admin/audit/auth-events?username={Uri.EscapeDataString(UserName)}&take={Take}");

        await Task.WhenAll(identityTask, authTask);

        var merged = new List<TimelineEntry>();

        if (identityTask.Result.IsSuccess)
        {
            foreach (var r in identityTask.Result.Value.Items)
            {
                merged.Add(new TimelineEntry(
                    Timestamp: r.ChangedAt,
                    Source:    "Admin action",
                    Label:     r.Action,
                    Note:      string.IsNullOrWhiteSpace(r.ChangedColumns)
                                  ? $"by {r.ChangedBy}"
                                  : $"{r.ChangedColumns.Replace('|', ',')} • by {r.ChangedBy}"));
            }
        }

        if (authTask.Result.IsSuccess)
        {
            foreach (var e in authTask.Result.Value.Items)
            {
                merged.Add(new TimelineEntry(
                    Timestamp: e.OccurredAt,
                    Source:    "Auth event",
                    Label:     e.EventType,
                    Note:      e.IpAddress is { Length: > 0 } ip
                                  ? $"from {ip}"
                                  : (e.Detail ?? "")));
            }
        }

        if (!identityTask.Result.IsSuccess && !authTask.Result.IsSuccess)
            _error = "Couldn't load activity for this user.";

        _entries = merged
            .OrderByDescending(x => x.Timestamp)
            .Take(Take * 2)
            .ToList();
    }

    private record TimelineEntry(DateTimeOffset Timestamp, string Source, string Label, string Note);
}
