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
}
