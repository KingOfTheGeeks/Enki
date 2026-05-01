using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Custom <see cref="IUserClaimsPrincipalFactory{TUser}"/> that derives
/// the <c>role=enki-admin</c> claim from
/// <see cref="ApplicationUser.IsEnkiAdmin"/> at sign-in time. The column
/// is the single source of truth — the role claim is never persisted as
/// an <c>AspNetUserClaim</c> row.
///
/// <para>
/// Per the Phase 8 follow-up review (Finding 3, Option B) this
/// collapses two stores of the same fact down to one. Previously the
/// seeder and the admin controller had to (1) flip the column,
/// (2) add/remove a role claim row, and (3) rotate the security stamp —
/// any writer that forgot one step shipped a desync. With the column
/// authoritative, writers only flip the column + rotate the stamp; the
/// next sign-in materialises the role claim from this factory.
/// </para>
///
/// <para>
/// Stale <c>role=enki-admin</c> claim rows that pre-date this factory
/// are filtered out before the column-derived value is added back, so a
/// dev DB seeded under the old write path doesn't ship duplicate or
/// stale role claims in tokens. A future migration can drop those rows
/// out of the table; until then this filter keeps the runtime correct.
/// </para>
/// </summary>
public sealed class EnkiUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole>    roleManager,
    IOptions<IdentityOptions>    options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
    {
        var principal = await base.CreateAsync(user);
        var identity  = (ClaimsIdentity)principal.Identity!;

        // Drop persisted role=enki-admin claims regardless of the column
        // value. The column is the truth; persisted rows are noise from
        // the pre-cutover seeder / controller paths.
        var stale = identity
            .FindAll(c => c.Type  == OpenIddictConstants.Claims.Role
                       && c.Value == AuthConstants.EnkiAdminRole)
            .ToList();
        foreach (var c in stale) identity.RemoveClaim(c);

        if (user.IsEnkiAdmin)
        {
            identity.AddClaim(new Claim(
                OpenIddictConstants.Claims.Role,
                AuthConstants.EnkiAdminRole));
        }

        // Classification claims — read by the WebApi's
        // CanAccessTenantHandler to decide tenant access for Tenant
        // users and (later) by feature gates that key off team
        // subtype. user_type is always present; team_subtype + tenant
        // are mutually exclusive per UserClassificationValidator.
        if (!string.IsNullOrEmpty(user.UserType?.Name))
        {
            identity.AddClaim(new Claim(
                AuthConstants.UserTypeClaim,
                user.UserType.Name));
        }
        if (!string.IsNullOrEmpty(user.TeamSubtype))
        {
            identity.AddClaim(new Claim(
                AuthConstants.TeamSubtypeClaim,
                user.TeamSubtype));
        }
        if (user.TenantId is { } tenantId && tenantId != Guid.Empty)
        {
            // Carry the GUID. The handler that needs to compare against
            // the route's {tenantCode} resolves the Code via a master-DB
            // lookup (cached, same shape as TenantRoutingMiddleware) —
            // emitting both the GUID and the code from here would require
            // master-DB access in this factory, which the Identity host
            // deliberately doesn't have.
            identity.AddClaim(new Claim(
                AuthConstants.TenantIdClaim,
                tenantId.ToString("D")));
        }

        return principal;
    }
}
