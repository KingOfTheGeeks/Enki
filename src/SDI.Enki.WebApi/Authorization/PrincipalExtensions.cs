using System.Security.Claims;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Convenience reads off the current <see cref="ClaimsPrincipal"/>.
/// Centralizes the "is this caller an Enki admin?" check so the same
/// claim-vs-role plumbing isn't repeated at every site that asks the
/// question. Single source for the role-check shape.
/// </summary>
public static class PrincipalExtensions
{
    /// <summary>
    /// True if the caller holds the cross-tenant <c>enki-admin</c>
    /// role. Looks at both <c>IsInRole</c> (which checks the claim
    /// type configured as the role-claim type on the principal) and
    /// the literal <c>"role"</c> claim — different auth schemes
    /// surface the role at different claim types depending on whether
    /// inbound claim mapping is on.
    /// </summary>
    public static bool HasEnkiAdminRole(this ClaimsPrincipal user) =>
        user.IsInRole(AuthConstants.EnkiAdminRole) ||
        user.HasClaim("role", AuthConstants.EnkiAdminRole);
}
