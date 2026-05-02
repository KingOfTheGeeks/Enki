using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// Default <see cref="IUserCapabilities"/>. Snapshot is built on
/// first access from <see cref="AuthenticationStateProvider"/>;
/// tenant memberships are loaded lazily on first tenant-scoped
/// check via <c>GET /me/memberships</c> and cached per-circuit.
/// </summary>
public sealed class UserCapabilities(
    AuthenticationStateProvider auth,
    IHttpClientFactory httpClientFactory) : IUserCapabilities
{
    private bool _snapshotLoaded;
    private bool _isAdmin;
    private bool _isTenantUser;
    private Guid? _boundTenantId;
    private TeamSubtype? _subtype;
    private HashSet<string> _capabilities = new(StringComparer.Ordinal);

    private HashSet<string>? _tenantMemberships;       // null = not yet loaded
    private readonly SemaphoreSlim _membershipLock = new(initialCount: 1, maxCount: 1);

    // ---- snapshot ----

    private async ValueTask EnsureSnapshotAsync()
    {
        if (_snapshotLoaded) return;
        var state = await auth.GetAuthenticationStateAsync();
        var user  = state.User;

        IsSignedIn = user.Identity?.IsAuthenticated ?? false;
        if (!IsSignedIn)
        {
            _snapshotLoaded = true;
            return;
        }

        _isAdmin       = user.IsInRole(AuthConstants.EnkiAdminRole)
                      || user.HasClaim("role", AuthConstants.EnkiAdminRole);
        _isTenantUser  = user.HasClaim(AuthConstants.UserTypeClaim, UserType.Tenant.Name);
        _boundTenantId = Guid.TryParse(user.FindFirst(AuthConstants.TenantIdClaim)?.Value, out var tid)
            ? tid
            : null;
        var rawSubtype = user.FindFirst(AuthConstants.TeamSubtypeClaim)?.Value;
        _subtype = TeamSubtype.TryFromName(rawSubtype, out var subtype) ? subtype : null;
        _capabilities = user.FindAll(EnkiClaimTypes.Capability)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        _snapshotLoaded = true;
    }

    private void EnsureSnapshotSync()
    {
        if (_snapshotLoaded) return;
        // Synchronous predicates need the snapshot; trigger the async
        // load and block. AuthenticationStateProvider is in-memory after
        // the circuit boots so this is a no-real-IO path.
        EnsureSnapshotAsync().AsTask().GetAwaiter().GetResult();
    }

    // ---- identity facts ----

    public bool IsSignedIn { get; private set; }

    public bool IsAdmin
    {
        get { EnsureSnapshotSync(); return _isAdmin; }
    }

    public bool IsTenantUser
    {
        get { EnsureSnapshotSync(); return _isTenantUser; }
    }

    public Guid? BoundTenantId
    {
        get { EnsureSnapshotSync(); return _boundTenantId; }
    }

    public TeamSubtype? Subtype
    {
        get { EnsureSnapshotSync(); return _subtype; }
    }

    public IReadOnlySet<string> CapabilityClaims
    {
        get { EnsureSnapshotSync(); return _capabilities; }
    }

    // ---- master-scoped predicates ----

    private bool HasSubtypeAtLeast(TeamSubtype min)
    {
        EnsureSnapshotSync();
        if (_isAdmin) return true;
        if (_isTenantUser) return false;
        return _subtype is not null && _subtype.Value >= min.Value;
    }

    public bool CanWriteMasterContent    => HasSubtypeAtLeast(TeamSubtype.Office);
    public bool CanDeleteMasterContent   => HasSubtypeAtLeast(TeamSubtype.Office);
    public bool CanManageMasterTools     => HasSubtypeAtLeast(TeamSubtype.Supervisor);
    public bool CanProvisionTenants      => HasSubtypeAtLeast(TeamSubtype.Supervisor);
    public bool CanManageTenantLifecycle => HasSubtypeAtLeast(TeamSubtype.Supervisor);
    public bool CanReadMasterRoster      => HasSubtypeAtLeast(TeamSubtype.Supervisor);

    public bool CanManageLicensing
    {
        get
        {
            EnsureSnapshotSync();
            if (_isAdmin) return true;
            if (_isTenantUser) return false;
            if (_subtype is not null && _subtype.Value >= TeamSubtype.Supervisor.Value) return true;
            return _capabilities.Contains(EnkiCapabilities.Licensing);
        }
    }

    public bool CanReachAdminArea
    {
        get { EnsureSnapshotSync(); return _isAdmin; }
    }

    // ---- per-target ----

    public bool CanManageUser(string? targetUserType)
    {
        EnsureSnapshotSync();
        if (_isAdmin) return true;
        if (_isTenantUser) return false;                          // Tenant users never manage other users
        if (string.Equals(targetUserType, UserType.Tenant.Name, StringComparison.Ordinal))
            return _subtype is not null && _subtype.Value >= TeamSubtype.Office.Value;
        return false;                                              // Team-target → admin only
    }

    public bool HasCapability(string capability)
    {
        EnsureSnapshotSync();
        return _capabilities.Contains(capability);
    }

    // ---- tenant-scoped predicates ----

    public async Task<bool> CanAccessTenantAsync(string tenantCode)
    {
        await EnsureSnapshotAsync();
        if (_isAdmin) return true;
        // Both Team and Tenant users go through the membership cache.
        // GET /me/memberships projects a Tenant user's bound tenant
        // code into TenantCodes (resolved server-side from their
        // tenant_id claim), so IsMemberOfAsync correctly returns true
        // for the bound code and false for anything else. An earlier
        // version of this predicate short-circuited "any code passes
        // for Tenant users" on the assumption they'd never reach a
        // foreign code — a typed-in URL breaks that, leaving the
        // sidebar pretending the user is inside a tenant the API
        // denies. The unified cache path makes UI gating agree with
        // server enforcement on direct URLs.
        return await IsMemberOfAsync(tenantCode);
    }

    public async Task<bool> CanWriteTenantContentAsync(string tenantCode)
    {
        await EnsureSnapshotAsync();
        if (_isAdmin) return true;
        if (_isTenantUser) return false;
        if (_subtype is null || _subtype.Value < TeamSubtype.Office.Value) return false;
        return await IsMemberOfAsync(tenantCode);
    }

    public async Task<bool> CanDeleteTenantContentAsync(string tenantCode)
        => await CanWriteTenantContentAsync(tenantCode);    // Same gate today.

    public async Task<bool> CanManageTenantMembersAsync(string tenantCode)
    {
        await EnsureSnapshotAsync();
        if (_isAdmin) return true;
        if (_isTenantUser) return false;
        if (_subtype is null || _subtype.Value < TeamSubtype.Supervisor.Value) return false;
        return await IsMemberOfAsync(tenantCode);
    }

    // ---- membership cache ----

    private async Task<bool> IsMemberOfAsync(string tenantCode)
    {
        await EnsureMembershipsAsync();
        return _tenantMemberships!.Contains(tenantCode);
    }

    private async Task EnsureMembershipsAsync()
    {
        if (_tenantMemberships is not null) return;
        await _membershipLock.WaitAsync();
        try
        {
            if (_tenantMemberships is not null) return;     // Double-check under lock.
            var client = httpClientFactory.CreateClient("EnkiApi");
            var result = await client.GetAsync<MyMembershipsDto>("me/memberships");
            // Fail-safe: if the API call fails, present an empty
            // membership set (every per-tenant gate denies). The user
            // sees no actions; the API returns 403 if they try anyway.
            _tenantMemberships = result.IsSuccess
                ? new HashSet<string>(result.Value.TenantCodes, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        finally { _membershipLock.Release(); }
    }
}

/// <summary>
/// Wire shape for <c>GET /me/memberships</c>. Carries the codes of
/// every tenant the caller is a member of. Admins return an empty
/// list with <c>IsAdmin = true</c> — the Blazor side short-circuits
/// before consulting this endpoint, but the flag is there as a
/// belt-and-braces signal.
/// </summary>
public sealed record MyMembershipsDto(
    bool IsAdmin,
    IReadOnlyList<string> TenantCodes);
