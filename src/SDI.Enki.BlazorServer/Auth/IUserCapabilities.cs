using SDI.Enki.Shared.Identity;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// Per-circuit capability oracle for the Blazor admin UI. Mirrors the
/// WebApi's <c>TeamAuthHandler</c> decision tree client-side so pages
/// can decide what to render without a network round-trip per check.
///
/// <para>
/// <b>UI gating is convenience; the API is the actual security.</b>
/// Hidden buttons are usability — the 403 from the API is what
/// actually stops a forged request. This service must agree with the
/// WebApi's policies; drift would surface as buttons that 403 when
/// clicked or hidden buttons the user could've used.
/// </para>
///
/// <para>
/// <b>Tenant-scoped checks are async.</b> They consult an in-memory
/// cache of the user's TenantUser memberships (loaded once per circuit
/// from <c>GET /me/memberships</c>). Master-scoped checks are
/// synchronous claim reads.
/// </para>
/// </summary>
public interface IUserCapabilities
{
    // ---- Identity facts (cheap reads off the principal) ----

    bool IsSignedIn   { get; }
    bool IsAdmin      { get; }
    bool IsTenantUser { get; }

    /// <summary>Bound tenant Id for Tenant users; null otherwise.</summary>
    Guid? BoundTenantId { get; }

    /// <summary>Team subtype for Team users; null for Tenant users / data-drift cases.</summary>
    TeamSubtype? Subtype { get; }

    /// <summary>Capability claim values held by the caller.</summary>
    IReadOnlySet<string> CapabilityClaims { get; }

    // ---- Master-scoped predicates (synchronous) ----

    bool CanWriteMasterContent    { get; }
    bool CanDeleteMasterContent   { get; }
    bool CanManageMasterTools     { get; }
    bool CanProvisionTenants      { get; }
    bool CanManageTenantLifecycle { get; }
    bool CanReadMasterRoster      { get; }
    bool CanManageLicensing       { get; }
    bool CanReachAdminArea        { get; }

    // ---- Tenant-scoped predicates (async; consult membership cache) ----

    Task<bool> CanAccessTenantAsync(string tenantCode);
    Task<bool> CanWriteTenantContentAsync(string tenantCode);
    Task<bool> CanDeleteTenantContentAsync(string tenantCode);
    Task<bool> CanManageTenantMembersAsync(string tenantCode);

    // ---- Per-target predicate (Identity admin pages) ----

    /// <summary>
    /// True when the current user can perform admin operations on the
    /// given target user. Mirrors the controller's
    /// <c>RequireSufficientAuthorityFor(target)</c>: admin can do
    /// anything; Office can manage Tenant-type targets only.
    /// </summary>
    bool CanManageUser(string? targetUserType);

    // ---- Capability check (escape hatch for future capabilities) ----

    bool HasCapability(string capability);
}
