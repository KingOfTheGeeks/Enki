using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin;

[Route("/admin/users/{Id}")]
[Layout(typeof(AdminLayout))]
// AdminUserDetail can be reached today only via the admin user list,
// which is admin-only. Office-tier management of Tenant users goes
// through the tenant members surface; opening that flow up to Office
// without giving them the cross-system roster view is the rule.
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class AdminUserDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public string Id { get; set; } = "";

    private AdminUserDetailDto? _user;
    private string? _loadError;
    private string? _actionError;
    private string? _temporaryPassword;
    private bool _busy;

    // Local edit state for the session-lifetime preset dropdown. The
    // committed value lives on the loaded _user.SessionLifetimeMinutes;
    // these track the operator's pending edit until they hit Apply.
    private string _pendingPreset = "default";
    private int? _pendingCustomMinutes;

    // Profile edit state — separate from the loaded _user so the operator
    // can revert via Discard. Synced from _user on every load and after
    // every successful save.
    private string  _editUserName     = "";
    private string  _editEmail        = "";
    private string? _editFirstName;
    private string? _editLastName;
    private string? _editTeamSubtype;
    private string  _editTenantIdString = "";
    private string? _profileError;
    private DateTimeOffset? _profileSavedAt;

    // Cached active-tenant list for the Tenant-user binding dropdown.
    // Loaded lazily — only fetched when the page actually shows the
    // Tenant binding selector (i.e. _user.UserType == "Tenant").
    private List<TenantSummaryDto>? _tenants;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiIdentity");
        var result = await client.GetAsync<AdminUserDetailDto>($"admin/users/{Id}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"User '{Id}' not found."
                : result.Error.AsAlertText();
            return;
        }
        _user = result.Value;
        SyncPendingFromLoaded();
        SyncProfileFromLoaded();
        SyncCapabilitiesFromLoaded();

        if (_user.UserType == "Tenant" && _tenants is null)
            await LoadTenantsAsync();
    }

    private async Task LoadTenantsAsync()
    {
        // Active tenants only — moving a user to an inactive tenant
        // would lock them out at the next request.
        var apiClient = HttpClientFactory.CreateClient("EnkiApi");
        var result = await apiClient.GetAsync<IEnumerable<TenantSummaryDto>>("tenants");
        if (!result.IsSuccess) { _profileError = result.Error.AsAlertText(); return; }
        _tenants = result.Value
            .Where(t => string.Equals(t.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Code)
            .ToList();
    }

    private void SyncProfileFromLoaded()
    {
        if (_user is null) return;
        _editUserName       = _user.UserName;
        _editEmail          = _user.Email;
        _editFirstName      = _user.FirstName;
        _editLastName       = _user.LastName;
        _editTeamSubtype    = _user.TeamSubtype ?? "Office";
        _editTenantIdString = _user.TenantId?.ToString("D") ?? "";
        _profileError       = null;
    }

    private bool IsProfileDirty()
    {
        if (_user is null) return false;
        if (!string.Equals(_editUserName, _user.UserName, StringComparison.Ordinal)) return true;
        if (!string.Equals(_editEmail,    _user.Email,    StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(_editFirstName ?? "", _user.FirstName ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(_editLastName  ?? "", _user.LastName  ?? "", StringComparison.Ordinal)) return true;

        if (_user.UserType == "Team")
        {
            if (!string.Equals(_editTeamSubtype, _user.TeamSubtype, StringComparison.Ordinal)) return true;
        }
        else if (_user.UserType == "Tenant")
        {
            var loaded = _user.TenantId?.ToString("D") ?? "";
            if (!string.Equals(_editTenantIdString, loaded, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void DiscardProfileEdits() => SyncProfileFromLoaded();

    private async Task SaveProfileAsync()
    {
        if (_user is null || _busy || !IsProfileDirty()) return;
        _busy = true; _profileError = null; _profileSavedAt = null;
        try
        {
            string?  teamSubtype = _user.UserType == "Team"   ? _editTeamSubtype : null;
            Guid?    tenantId    = _user.UserType == "Tenant" && Guid.TryParse(_editTenantIdString, out var tid)
                ? tid
                : null;

            var dto = new UpdateUserDto(
                UserName:         _editUserName.Trim(),
                Email:            _editEmail.Trim(),
                FirstName:        string.IsNullOrWhiteSpace(_editFirstName) ? null : _editFirstName.Trim(),
                LastName:         string.IsNullOrWhiteSpace(_editLastName)  ? null : _editLastName.Trim(),
                TeamSubtype:      teamSubtype,
                TenantId:         tenantId,
                ConcurrencyStamp: _user.ConcurrencyStamp);

            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var result = await client.PutAsync($"admin/users/{Id}", dto);
            if (!result.IsSuccess) { _profileError = result.Error.AsAlertText(); return; }
            _profileSavedAt = DateTimeOffset.UtcNow;
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// Reset the dropdown + custom-minutes box to reflect whatever's
    /// currently committed on the user. Called on initial load and after
    /// each successful Apply so the form reads as "no pending change".
    /// </summary>
    private void SyncPendingFromLoaded()
    {
        if (_user is null) return;
        var minutes = _user.SessionLifetimeMinutes;
        _pendingCustomMinutes = null;
        _pendingPreset = minutes switch
        {
            null      => "default",
            480       => "480",
            1440      => "1440",
            10080     => "10080",
            43200     => "43200",
            525600    => "525600",
            _         => "custom",
        };
        if (_pendingPreset == "custom") _pendingCustomMinutes = minutes;
    }

    private void OnPresetChanged()
    {
        // Clear the custom value when leaving the custom slot so it
        // doesn't leak across selections; pre-fill it with the loaded
        // value when entering custom from a non-preset state.
        if (_pendingPreset != "custom")
        {
            _pendingCustomMinutes = null;
        }
        else if (_user is not null && _user.SessionLifetimeMinutes is int loaded)
        {
            _pendingCustomMinutes = loaded;
        }
    }

    private int? PendingMinutes() => _pendingPreset switch
    {
        "default" => null,
        "custom"  => _pendingCustomMinutes,
        _         => int.TryParse(_pendingPreset, out var m) ? m : null,
    };

    private bool IsPendingValueDifferent()
    {
        if (_user is null) return false;
        // Custom selected with no number entered yet — disable Apply
        // until the operator types something positive.
        if (_pendingPreset == "custom" && _pendingCustomMinutes is null or <= 0) return false;
        return PendingMinutes() != _user.SessionLifetimeMinutes;
    }

    private async Task ApplySessionLifetimeAsync()
    {
        if (_busy || _user is null) return;
        if (!IsPendingValueDifferent()) return;
        _busy = true; _actionError = null; _temporaryPassword = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var result = await client.PostAsync(
                $"admin/users/{Id}/session-lifetime",
                new SetSessionLifetimeDto(PendingMinutes(), _user.ConcurrencyStamp));
            if (!result.IsSuccess)
            {
                _actionError = result.Error.AsAlertText();
                return;
            }
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    private static string SessionLifetimeDisplay(int? minutes) => minutes switch
    {
        null      => "Default (1 hour)",
        480       => "8 hours",
        1440      => "24 hours",
        10080     => "7 days",
        43200     => "30 days",
        525600    => "1 year",
        var m     => $"{m} minutes",
    };

    private async Task SetAdminAsync(bool isAdmin)
    {
        if (_user is null) return;
        await PerformAsync(client => client.PostAsync(
            $"admin/users/{Id}/admin",
            new SetAdminRoleDto(isAdmin, _user.ConcurrencyStamp)));
    }

    private async Task LockAsync()
    {
        if (_user is null) return;
        await PerformAsync(client => client.PostAsync(
            $"admin/users/{Id}/lock",
            new AdminUserActionDto(_user.ConcurrencyStamp)));
    }

    private async Task UnlockAsync()
    {
        if (_user is null) return;
        await PerformAsync(client => client.PostAsync(
            $"admin/users/{Id}/unlock",
            new AdminUserActionDto(_user.ConcurrencyStamp)));
    }

    private async Task ResetPasswordAsync()
    {
        if (_busy || _user is null) return;
        _busy = true; _actionError = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var result = await client.PostAsync<AdminUserActionDto, ResetPasswordResponseDto>(
                $"admin/users/{Id}/reset-password",
                new AdminUserActionDto(_user.ConcurrencyStamp));
            if (!result.IsSuccess)
            {
                _actionError = $"Reset password failed: {result.Error.AsAlertText()}";
                return;
            }
            _temporaryPassword = result.Value.TemporaryPassword;
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    private async Task PerformAsync(Func<HttpClient, Task<ApiResult>> call)
    {
        if (_busy) return;
        _busy = true; _actionError = null; _temporaryPassword = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var result = await call(client);
            if (!result.IsSuccess)
            {
                _actionError = result.Error.AsAlertText();
                return;
            }
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    private static string Dashed(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string DashClass(string? s) => string.IsNullOrWhiteSpace(s) ? "enki-dash" : "";

    // ---------- capabilities ----------

    private HashSet<string> _heldCapabilities = new(StringComparer.Ordinal);
    private string? _capabilityError;

    private void SyncCapabilitiesFromLoaded()
    {
        _heldCapabilities = _user is null
            ? new HashSet<string>()
            : new HashSet<string>(_user.Capabilities, StringComparer.Ordinal);
    }

    private async Task ToggleCapabilityAsync(string capability, bool grant)
    {
        if (_user is null || _busy) return;
        _busy = true; _capabilityError = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var path = $"admin/users/{Id}/capabilities/{capability}";
            var result = grant
                ? await client.PostAsync(path)
                : await HttpClientApiExtensions.DeleteAsync(client, path);
            if (!result.IsSuccess)
            {
                _capabilityError = result.Error.AsAlertText();
                return;
            }
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>Human label for a capability. Add to this switch when a new capability ships.</summary>
    private static string CapabilityDisplay(string capability) => capability switch
    {
        EnkiCapabilities.Licensing => "Licensing",
        _                          => capability,
    };

    /// <summary>Short hint shown under the checkbox.</summary>
    private static string CapabilityHint(string capability) => capability switch
    {
        EnkiCapabilities.Licensing => "Allows generating and revoking licenses regardless of TeamSubtype.",
        _                          => string.Empty,
    };
}
