namespace SDI.Enki.Shared.Authorization;

/// <summary>
/// Policy name constants — reference these rather than typing string
/// literals at each <c>[Authorize(Policy = "...")]</c> so typos fail at
/// compile time. Policies are constructed on the WebApi side from the
/// single <c>TeamAuthRequirement</c>; on the BlazorServer side from
/// claim-assertion policies that mirror the same gates.
///
/// <para>
/// Living in <c>SDI.Enki.Shared</c> so both hosts reference the same
/// constants — a renamed policy can't drift out of sync between the
/// API attributes and the Blazor page attributes.
/// </para>
///
/// <para>
/// The matrix in <c>docs/sop-authorization-redesign.md</c> (section E)
/// is the human-readable view of who can hit each policy.
/// </para>
/// </summary>
public static class EnkiPolicies
{
    /// <summary>Any signed-in caller with the <c>enki</c> scope. Default fallback policy.</summary>
    public const string EnkiApiScope = "EnkiApiScope";

    // ---- tenant-scoped (require {tenantCode} on the route) ----

    /// <summary>
    /// Tenant member or admin. Field-equivalent for Tenant-type users
    /// (also matches when their bound <c>tenant_id</c> resolves to the
    /// route's tenant code). Used for tenant-scoped READ and Runs/Shots
    /// writes.
    /// </summary>
    public const string CanAccessTenant = "CanAccessTenant";

    /// <summary>
    /// Office-or-above tenant member, or admin. Used for tenant-scoped
    /// content writes (Jobs, Wells, TieOns, Surveys, Tubulars,
    /// Formations, CommonMeasures, Magnetics, Logs, Comments).
    /// </summary>
    public const string CanWriteTenantContent = "CanWriteTenantContent";

    /// <summary>
    /// Office-or-above tenant member, or admin. Same gate as
    /// <see cref="CanWriteTenantContent"/> today; kept as a separate
    /// policy so a future "delete needs Supervisor" tightening is a
    /// one-line policy change.
    /// </summary>
    public const string CanDeleteTenantContent = "CanDeleteTenantContent";

    /// <summary>
    /// Supervisor-or-above tenant member, or admin. Tenant-scoped —
    /// caller must be a member of the route's tenant before the
    /// subtype gate applies. Used by tenant-membership management
    /// endpoints.
    /// </summary>
    public const string CanManageTenantMembers = "CanManageTenantMembers";

    // ---- master-scoped (no tenant context required) ----

    /// <summary>Office-or-above or admin. Master content writes (Calibrations, Tenant settings, Tenant-user create, master sync).</summary>
    public const string CanWriteMasterContent = "CanWriteMasterContent";

    /// <summary>Office-or-above or admin. Master content deletes (Calibrations).</summary>
    public const string CanDeleteMasterContent = "CanDeleteMasterContent";

    /// <summary>Supervisor-or-above or admin. Master Tools — fleet-wide CRUD.</summary>
    public const string CanManageMasterTools = "CanManageMasterTools";

    /// <summary>Supervisor-or-above or admin. Tenant provisioning (creates SQL DBs).</summary>
    public const string CanProvisionTenants = "CanProvisionTenants";

    /// <summary>Supervisor-or-above or admin. Tenant lifecycle (deactivate, reactivate, archive).</summary>
    public const string CanManageTenantLifecycle = "CanManageTenantLifecycle";

    /// <summary>Supervisor-or-above or admin. Master user-picker / roster reads.</summary>
    public const string CanReadMasterRoster = "CanReadMasterRoster";

    /// <summary>
    /// Supervisor-or-above OR holding the <c>licensing</c> capability
    /// claim, or admin. License generation and revocation.
    /// </summary>
    public const string CanManageLicensing = "CanManageLicensing";

    /// <summary>System-admin only. Cross-tenant administrative endpoints (system settings, audit feeds).</summary>
    public const string EnkiAdminOnly = "EnkiAdminOnly";
}
