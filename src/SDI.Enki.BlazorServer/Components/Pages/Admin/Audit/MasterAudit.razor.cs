using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin.Audit;

[Route("/admin/audit/master")]
[Layout(typeof(AdminLayout))]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class MasterAudit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    private const int PageSize = 50;

    private DateTimeOffset? _from;
    private DateTimeOffset? _to;
    private string _entityType = "";
    private string _action = "";
    private string _changedBy = "";
    private int _skip;
    private int _total;
    private List<AuditLogEntryDto>? _rows;
    private string? _error;

    private bool CanGoPrev => _skip > 0;
    private bool CanGoNext => _skip + PageSize < _total;

    protected override async Task OnInitializedAsync()
    {
        ReadQueryString();
        if (_from is null && _to is null)
        {
            // Default 7-day window on first paint (per the audit-UI design call).
            _from = DateTimeOffset.UtcNow.AddDays(-7);
        }
        await FetchAsync();
    }

    /// <summary>Hydrate filter state from the URL so /admin/audit/master?entityType=AuthzDenial is deep-linkable.</summary>
    private void ReadQueryString()
    {
        var uri = Nav.ToAbsoluteUri(Nav.Uri);
        var q = QueryHelpers.ParseQuery(uri.Query);
        if (q.TryGetValue("from", out var fv) && DateTimeOffset.TryParse(fv, out var f)) _from = f;
        if (q.TryGetValue("to", out var tv) && DateTimeOffset.TryParse(tv, out var t)) _to = t;
        if (q.TryGetValue("entityType", out var et)) _entityType = et!;
        if (q.TryGetValue("action", out var a)) _action = a!;
        if (q.TryGetValue("changedBy", out var cb)) _changedBy = cb!;
        if (q.TryGetValue("skip", out var s) && int.TryParse(s, out var sk)) _skip = Math.Max(0, sk);
    }

    /// <summary>
    /// Filter-change handler. Updates the URL so the back-button + page
    /// refresh land on the same view, then re-fetches. NOT called from
    /// OnInitializedAsync — calling NavigateTo during component init
    /// triggers a render-lifecycle race in InteractiveServer mode.
    /// </summary>
    private async Task ReloadAsync()
    {
        var path = Nav.ToAbsoluteUri(Nav.Uri).AbsolutePath;
        Nav.NavigateTo(path + "?" + BuildQuery(includeSkipTake: true), forceLoad: false, replace: true);
        await FetchAsync();
    }

    private async Task FetchAsync()
    {
        _rows = null;
        _error = null;
        StateHasChanged();

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var path = $"admin/audit/master?{BuildQuery(includeSkipTake: true)}";

        var result = await client.GetAsync<PagedResult<AuditLogEntryDto>>(path);
        if (!result.IsSuccess)
        {
            _error = result.Error.AsAlertText();
            _rows = [];
            return;
        }

        _rows  = result.Value.Items.ToList();
        _total = result.Value.Total;
    }

    private string BuildQuery(bool includeSkipTake)
    {
        var parts = new List<string>();
        if (_from is { } f)                                  parts.Add($"from={Uri.EscapeDataString(f.UtcDateTime.ToString("O"))}");
        if (_to is { } t)                                    parts.Add($"to={Uri.EscapeDataString(t.UtcDateTime.ToString("O"))}");
        if (!string.IsNullOrWhiteSpace(_entityType))         parts.Add($"entityType={Uri.EscapeDataString(_entityType)}");
        if (!string.IsNullOrWhiteSpace(_action))             parts.Add($"action={Uri.EscapeDataString(_action)}");
        if (!string.IsNullOrWhiteSpace(_changedBy))          parts.Add($"changedBy={Uri.EscapeDataString(_changedBy)}");
        if (includeSkipTake)
        {
            parts.Add($"skip={_skip}");
            parts.Add($"take={PageSize}");
        }
        return string.Join("&", parts);
    }

    private string CsvPath => $"admin/audit/master/csv?{BuildQuery(includeSkipTake: false)}";
    private string CsvDownloadName => $"enki-master-audit-{DateTimeOffset.Now:yyyy-MM-dd}.csv";

    private async Task OnEntityTypeChanged(ChangeEventArgs e)
    {
        _entityType = (string?)e.Value ?? "";
        _skip = 0;
        await ReloadAsync();
    }

    private async Task OnActionChanged(ChangeEventArgs e)
    {
        _action = (string?)e.Value ?? "";
        _skip = 0;
        await ReloadAsync();
    }

    private async Task OnChangedByChanged(ChangeEventArgs e)
    {
        _changedBy = (string?)e.Value ?? "";
        _skip = 0;
        await ReloadAsync();
    }

    private async Task ShowDenialsOnly()
    {
        _entityType = "AuthzDenial";
        _action = "";
        _skip = 0;
        await ReloadAsync();
    }

    private async Task PrevPageAsync()
    {
        if (!CanGoPrev) return;
        _skip = Math.Max(0, _skip - PageSize);
        await ReloadAsync();
    }

    private async Task NextPageAsync()
    {
        if (!CanGoNext) return;
        _skip += PageSize;
        await ReloadAsync();
    }
}
