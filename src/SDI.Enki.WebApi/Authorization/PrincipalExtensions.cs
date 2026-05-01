using System.Security.Claims;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Convenience reads off the current <see cref="ClaimsPrincipal"/>.
/// Centralises the "is this caller an Enki admin?", "what's their
/// subtype?", "do they hold capability X?" checks so the same claim
/// plumbing isn't repeated at every site that asks the question.
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

    /// <summary>
    /// True iff the caller's <c>user_type</c> claim is <c>Tenant</c>.
    /// </summary>
    public static bool IsTenantTypeUser(this ClaimsPrincipal user) =>
        user.HasClaim(AuthConstants.UserTypeClaim, UserType.Tenant.Name);

    /// <summary>
    /// Returns the caller's <see cref="TeamSubtype"/> from the
    /// <c>team_subtype</c> claim, or <c>null</c> when missing /
    /// malformed. Tenant users always return null; Team users with a
    /// classified row return a value.
    /// </summary>
    public static TeamSubtype? GetTeamSubtype(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst(AuthConstants.TeamSubtypeClaim)?.Value;
        if (string.IsNullOrEmpty(raw)) return null;
        return TeamSubtype.TryFromName(raw, out var subtype) ? subtype : null;
    }

    /// <summary>
    /// True iff the caller's <see cref="TeamSubtype"/> is
    /// <paramref name="minimum"/> or higher in the Field &lt; Office &lt;
    /// Supervisor ordering. Returns false for missing / malformed
    /// subtype claims (fail-safe: missing subtype never elevates).
    /// </summary>
    public static bool HasTeamSubtypeAtLeast(this ClaimsPrincipal user, TeamSubtype minimum)
    {
        var subtype = user.GetTeamSubtype();
        return subtype is not null && subtype.Value >= minimum.Value;
    }

    /// <summary>
    /// True iff the caller carries an <c>enki:capability</c> claim
    /// with the given value. Capability values come from
    /// <see cref="EnkiCapabilities"/>.
    /// </summary>
    public static bool HasCapability(this ClaimsPrincipal user, string capability) =>
        user.HasClaim(EnkiClaimTypes.Capability, capability);
}
