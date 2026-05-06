using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin;

[Route("/admin/users/new")]
[Layout(typeof(AdminLayout))]
// Same rationale as AdminUserDetail — the admin user surface is the
// cross-system roster + creator. Office staff create Tenant users
// through the tenant members workflow, not here.
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class AdminUserCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    private string  _userName     = "";
    private string  _email        = "";
    private string? _firstName;
    private string? _lastName;
    // Office sees only the Tenant option, so default to Tenant in that
    // case; admin defaults to Team (the more common SDI-internal flow).
    // OnInitialized resets this once IUserCapabilities is populated.
    private string  _userType     = "Team";
    private string? _teamSubtype  = "Office";
    private string  _tenantIdString = "";

    protected override void OnInitialized()
    {
        if (!Capabilities.IsAdmin)
        {
            _userType = "Tenant";
            _teamSubtype = null;
        }
    }

    private List<TenantSummaryDto>? _tenants;
    private string? _error;
    private bool _busy;

    private string? _temporaryPassword;
    private string? _createdId;

    // Master-DB sync orchestration. After the Identity create succeeds,
    // Team users get a follow-up POST /admin/master-users/sync to create
    // the matching master User row (Tenant users skip this — they don't
    // appear in master-side membership tables). If that second call
    // fails, the Identity user already exists and the operator can
    // retry from the banner. Tenant users have _masterSyncFailed=false
    // and _masterUserId=null because they never attempted a sync.
    private bool  _masterSyncFailed;
    private string? _masterSyncError;
    private Guid? _masterUserId;

    private void SetUserType(string newType)
    {
        _userType = newType;
        if (newType == "Tenant")
        {
            _teamSubtype = null;
            // Lazy-load tenants the first time the operator selects Tenant.
            if (_tenants is null) _ = LoadTenantsAsync();
        }
        else
        {
            _teamSubtype = "Office";
            _tenantIdString = "";
        }
    }

    private async Task LoadTenantsAsync()
    {
        var apiClient = HttpClientFactory.CreateClient("EnkiApi");
        var result = await apiClient.GetAsync<IEnumerable<TenantSummaryDto>>("tenants");
        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _tenants = result.Value
            .Where(t => string.Equals(t.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Code)
            .ToList();
        StateHasChanged();
    }

    private bool CanSubmit()
    {
        if (string.IsNullOrWhiteSpace(_userName)) return false;
        if (string.IsNullOrWhiteSpace(_email))    return false;
        if (_userType == "Team"   && string.IsNullOrWhiteSpace(_teamSubtype)) return false;
        if (_userType == "Tenant" && !Guid.TryParse(_tenantIdString, out _))  return false;
        return true;
    }

    private async Task CreateAsync()
    {
        if (_busy || !CanSubmit()) return;
        _busy = true; _error = null;
        try
        {
            var dto = new CreateUserDto(
                UserName:   _userName.Trim(),
                Email:      _email.Trim(),
                FirstName:  string.IsNullOrWhiteSpace(_firstName) ? null : _firstName.Trim(),
                LastName:   string.IsNullOrWhiteSpace(_lastName)  ? null : _lastName.Trim(),
                UserType:   _userType,
                TeamSubtype: _userType == "Team" ? _teamSubtype : null,
                TenantId:   _userType == "Tenant" && Guid.TryParse(_tenantIdString, out var tid) ? tid : null);

            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var result = await client.PostAsync<CreateUserDto, CreateUserResponseDto>("admin/users", dto);
            if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }

            _temporaryPassword = result.Value.TemporaryPassword;
            _createdId         = result.Value.Id;

            // Team users get a master-DB User row via the WebApi sync
            // endpoint. Tenant users skip this — they don't appear in
            // master-side tenant-membership tables and have no need
            // for a master User.Id alias.
            if (_userType == "Team" && Guid.TryParse(_createdId, out var identityGuid))
            {
                await SyncMasterUserAsync(identityGuid, BuildDisplayName());
            }
        }
        finally { _busy = false; }
    }

    private async Task RetryMasterSyncAsync()
    {
        if (_busy || _createdId is null) return;
        if (!Guid.TryParse(_createdId, out var identityGuid)) return;
        _busy = true;
        try { await SyncMasterUserAsync(identityGuid, BuildDisplayName()); }
        finally { _busy = false; }
    }

    private async Task SyncMasterUserAsync(Guid identityId, string displayName)
    {
        _masterSyncFailed = false;
        _masterSyncError  = null;
        var apiClient = HttpClientFactory.CreateClient("EnkiApi");
        var sync = await apiClient.PostAsync<SyncMasterUserDto, SyncMasterUserResponseDto>(
            "admin/master-users/sync",
            new SyncMasterUserDto(identityId, displayName));
        if (!sync.IsSuccess)
        {
            _masterSyncFailed = true;
            _masterSyncError  = sync.Error.AsAlertText();
            return;
        }
        _masterUserId = sync.Value.UserId;
    }

    private string BuildDisplayName()
    {
        var first = (_firstName ?? "").Trim();
        var last  = (_lastName  ?? "").Trim();
        var full  = $"{first} {last}".Trim();
        // Fall back to the username when no name fields were supplied
        // so the master User row never lands with a blank Name (the
        // column is required and the picker would render empty rows).
        return string.IsNullOrEmpty(full) ? _userName.Trim() : full;
    }
}
