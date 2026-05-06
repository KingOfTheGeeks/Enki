using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin.Audit;

[Route("/admin/audit/auth-events")]
[Layout(typeof(AdminLayout))]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class AuthEvents : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    private const int PageSize = 50;
    private const int UaTruncate = 80;

    private DateTimeOffset? _from;
    private DateTimeOffset? _to;
    private string _username = "";
    private string _eventType = "";
    private int _skip;
    private int _total;
    private List<AuthEventEntryDto>? _rows;
    private string? _error;

    private bool CanGoPrev => _skip > 0;
    private bool CanGoNext => _skip + PageSize < _total;

    protected override async Task OnInitializedAsync()
    {
        ReadQueryString();
        if (_from is null && _to is null)
            _from = DateTimeOffset.UtcNow.AddDays(-7);
        await FetchAsync();
    }

    private void ReadQueryString()
    {
        var uri = Nav.ToAbsoluteUri(Nav.Uri);
        var q = QueryHelpers.ParseQuery(uri.Query);
        if (q.TryGetValue("from", out var fv) && DateTimeOffset.TryParse(fv, out var f)) _from = f;
        if (q.TryGetValue("to", out var tv) && DateTimeOffset.TryParse(tv, out var t)) _to = t;
        if (q.TryGetValue("username", out var u))  _username  = u!;
        if (q.TryGetValue("eventType", out var et)) _eventType = et!;
        if (q.TryGetValue("skip", out var s) && int.TryParse(s, out var sk)) _skip = Math.Max(0, sk);
    }

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

        var client = HttpClientFactory.CreateClient("EnkiIdentity");
        var path = $"admin/audit/auth-events?{BuildQuery(includeSkipTake: true)}";

        var result = await client.GetAsync<PagedResult<AuthEventEntryDto>>(path);
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
        if (!string.IsNullOrWhiteSpace(_username))           parts.Add($"username={Uri.EscapeDataString(_username)}");
        if (!string.IsNullOrWhiteSpace(_eventType))          parts.Add($"eventType={Uri.EscapeDataString(_eventType)}");
        if (includeSkipTake)
        {
            parts.Add($"skip={_skip}");
            parts.Add($"take={PageSize}");
        }
        return string.Join("&", parts);
    }

    private string CsvPath => $"admin/audit/auth-events/csv?{BuildQuery(includeSkipTake: false)}";
    private string CsvDownloadName => $"enki-auth-events-{DateTimeOffset.Now:yyyy-MM-dd}.csv";

    private async Task OnUsernameChanged(ChangeEventArgs e)
    {
        _username = (string?)e.Value ?? "";
        _skip = 0;
        await ReloadAsync();
    }

    private async Task OnEventTypeChanged(ChangeEventArgs e)
    {
        _eventType = (string?)e.Value ?? "";
        _skip = 0;
        await ReloadAsync();
    }

    private async Task ShowFailuresOnly()
    {
        _eventType = "SignInFailed";
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

    private static string TruncateUa(string ua) =>
        ua.Length <= UaTruncate ? ua : ua[..UaTruncate] + "…";
}
