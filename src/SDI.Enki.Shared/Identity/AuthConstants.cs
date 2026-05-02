namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Authentication / authorization string contracts that have to stay
/// in sync between the Identity host (which issues tokens), the WebApi
/// host (which validates them and enforces policies), and the Blazor
/// client (which requests them). Living in <see cref="SDI.Enki.Shared"/>
/// because all three projects already reference it.
///
/// <para>
/// Drift here fails closed — policies stop matching, tests break — so
/// the centralisation is more about removing one source of footgun than
/// preventing a security regression. Previously the same literals were
/// copy-pasted across an <c>IdentitySeedData.WebApiScope</c> re-export,
/// a <c>WebApi/Program.cs IdentitySeedConstants</c> shadow, and a per-
/// handler <c>AdminRole</c> constant on the (now-retired) per-policy
/// handlers — collapsed into <see cref="EnkiAdminRole"/> here.
/// </para>
/// </summary>
public static class AuthConstants
{
    /// <summary>
    /// OAuth scope identifying the Enki WebApi resource. Issued by
    /// Identity, requested by the Blazor client, validated by WebApi.
    /// </summary>
    public const string WebApiScope = "enki";

    /// <summary>
    /// Role claim value granting cross-tenant admin access. Read by
    /// the WebApi's <c>TeamAuthHandler</c> (step 2 of the decision
    /// tree) as the short-circuit that bypasses every subtype,
    /// membership, and capability check. Emitted as a direct user
    /// claim (not via AspNet Identity roles) to keep the storage path
    /// independent of role-store seeding.
    /// </summary>
    public const string EnkiAdminRole = "enki-admin";

    /// <summary>
    /// Claim type carrying the user's top-level <see cref="UserType"/>
    /// (Team / Tenant). Always present on issued tokens. Read by the
    /// WebApi's <c>TeamAuthHandler</c> (step 3) to fork between the
    /// Team-side TenantUser membership check and the Tenant-side
    /// hard-binding check.
    /// </summary>
    public const string UserTypeClaim = "user_type";

    /// <summary>
    /// Claim type carrying a Team user's <see cref="TeamSubtype"/>
    /// (Field / Office / Supervisor). Present only when
    /// <c>UserType == Team</c>; absent on Tenant users.
    /// </summary>
    public const string TeamSubtypeClaim = "team_subtype";

    /// <summary>
    /// Claim type carrying a Tenant user's bound <c>Tenants.Id</c> (GUID,
    /// "D" format). Present only when <c>UserType == Tenant</c>; absent
    /// on Team users. The handler that compares against the route's
    /// <c>{tenantCode}</c> resolves the Code via a master-DB lookup —
    /// emitting the code directly would require master-DB access in
    /// the Identity host, which it deliberately doesn't have.
    /// </summary>
    public const string TenantIdClaim = "tenant_id";
}
