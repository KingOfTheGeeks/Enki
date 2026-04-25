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
/// a <c>WebApi/Program.cs IdentitySeedConstants</c> shadow, and
/// <c>CanAccessTenantHandler.AdminRole</c>.
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
    /// <c>CanAccessTenantHandler</c> as the membership-check bypass.
    /// Emitted as a direct user claim (not via AspNet Identity roles)
    /// to keep the storage path independent of role-store seeding.
    /// </summary>
    public const string EnkiAdminRole = "enki-admin";
}
