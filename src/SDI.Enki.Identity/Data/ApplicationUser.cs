using Microsoft.AspNetCore.Identity;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Enki's <see cref="IdentityUser"/> extension. Deliberately thin — profile
/// data lives on the master-DB User entity (linked by <c>IdentityId</c> == this
/// user's <c>Id</c>), so AspNetUsers carries only auth + email + phone.
///
/// <c>UserType</c> is preserved from legacy for forward-compat; today it's
/// just "Team" (SDI employee). When external tenant users come online, expect
/// values like "TenantExternal" or per-tenant discriminators.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string? UserType { get; set; }

    /// <summary>
    /// Cross-tenant SDI admin flag. When true, the seed path adds a
    /// <c>role=enki-admin</c> claim on the user; OpenIddict emits it in
    /// the access token; WebApi's <c>CanAccessTenantHandler</c> short-
    /// circuits the TenantUser membership check and grants access to
    /// every tenant.
    ///
    /// Kept here (not on the master-DB <c>User</c>) because token issuance
    /// reads the ApplicationUser in <c>AuthorizationController.Authorize</c>
    /// and never touches the master DB. A future admin UI will flip this
    /// bit + rotate the user's security stamp so the next token picks it up.
    /// </summary>
    public bool IsEnkiAdmin { get; set; }
}
