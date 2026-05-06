using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{Code}/members")]
// Tenant-scoped policy can't be evaluated by [Authorize(Policy=...)]
// alone — it needs the {Code} route value. Page-level [Authorize]
// gates the cookie; the OnInitializedAsync probe redirects to
// /forbidden if the caller isn't a Supervisor+ member of THIS tenant
// (or admin). API endpoints enforce the same predicate; this is just
// visual short-circuit.
[Authorize]
public partial class TenantMembers : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public string Code { get; set; } = "";

    private List<TenantMemberDto>? _members;
    private List<MasterUserSummaryDto> _allUsers = new();
    private string? _listError;
    private string? _actionError;
    private string  _newUserId = "";
    private bool    _busy;

    private IEnumerable<MasterUserSummaryDto> CandidateUsers =>
        _members is null
            ? _allUsers
            : _allUsers.Where(u => !_members.Any(m => m.UserId == u.UserId));

    protected override async Task OnInitializedAsync()
    {
        // Tenant-scoped capability gate. CanManageTenantMembersAsync runs
        // a /me/memberships probe (cached per-circuit) so the second hit
        // is in-memory. Redirect lands on /forbidden with breadcrumbs;
        // the API enforces the same predicate so a Field user navigating
        // here directly still gets stopped — this is just visual short
        // circuit so they don't see a half-loaded page first.
        if (!await Capabilities.CanManageTenantMembersAsync(Code))
        {
            Nav.NavigateTo($"/forbidden?required=Supervisor&resource=Members+%2F+{Code}", forceLoad: false);
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var membersResult = await client.GetAsync<List<TenantMemberDto>>($"tenants/{Code}/members");
        if (!membersResult.IsSuccess) { _listError = membersResult.Error.AsAlertText(); return; }
        _members = membersResult.Value;

        var usersResult = await client.GetAsync<List<MasterUserSummaryDto>>("admin/master-users");
        if (!usersResult.IsSuccess) { _listError = usersResult.Error.AsAlertText(); return; }
        _allUsers = usersResult.Value;
    }

    private async Task AddAsync()
    {
        if (_busy || string.IsNullOrEmpty(_newUserId)) return;
        _busy = true; _actionError = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var dto = new AddTenantMemberDto(Guid.Parse(_newUserId));
            var result = await client.PostAsync($"tenants/{Code}/members", dto);
            if (!result.IsSuccess)
            {
                _actionError = result.Error.AsAlertText();
                return;
            }
            _newUserId = "";
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    private async Task RemoveAsync(Guid userId, string username)
    {
        if (_busy) return;
        _busy = true; _actionError = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            // Explicit static-call form: HttpClient has an instance
            // DeleteAsync(string) that returns HttpResponseMessage, which
            // would win overload resolution against our extension.
            var result = await HttpClientApiExtensions.DeleteAsync(client,
                $"tenants/{Code}/members/{userId}");
            if (!result.IsSuccess)
            {
                _actionError = $"Remove '{username}': {result.Error.AsAlertText()}";
                return;
            }
            await LoadAsync();
        }
        finally { _busy = false; }
    }
}
