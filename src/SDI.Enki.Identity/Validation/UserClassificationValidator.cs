using SDI.Enki.Shared.Identity;

namespace SDI.Enki.Identity.Validation;

/// <summary>
/// Single source of truth for the validation rules on the
/// <c>UserType</c> + <c>TeamSubtype</c> + <c>TenantId</c> +
/// <c>IsEnkiAdmin</c> tuple. Both the admin endpoints and the seed
/// reconciler call this before writing — drift here would mean the
/// admin UI accepts combinations the seed rejects (or vice versa),
/// which would surface as silent state corruption on the next reseed.
///
/// <para>
/// Rules:
/// </para>
/// <list type="number">
///   <item><c>UserType</c> is required.</item>
///   <item>Team ⇒ <c>TeamSubtype</c> set, <c>TenantId</c> null.</item>
///   <item>Tenant ⇒ <c>TenantId</c> set + non-empty Guid,
///   <c>TeamSubtype</c> null, <c>IsEnkiAdmin</c> false. Tenant users
///   can never hold cross-tenant admin rights.</item>
/// </list>
///
/// <para>
/// Returns a list of <see cref="ValidationFailure"/>; empty list = OK.
/// Each failure carries the offending field name (matching the DTO /
/// column name) so the admin endpoint can hand the failures to
/// <c>ModelState</c> and produce a normal RFC 7807 ValidationProblem.
/// </para>
/// </summary>
public static class UserClassificationValidator
{
    public sealed record ValidationFailure(string Field, string Message);

    public static IReadOnlyList<ValidationFailure> Validate(
        UserType?    userType,
        TeamSubtype? teamSubtype,
        Guid?        tenantId,
        bool         isEnkiAdmin)
    {
        var failures = new List<ValidationFailure>(4);

        if (userType is null)
        {
            failures.Add(new("UserType", "UserType is required (Team or Tenant)."));
            return failures;
        }

        if (userType == UserType.Team)
        {
            if (teamSubtype is null)
                failures.Add(new("TeamSubtype",
                    "TeamSubtype is required for Team users (Field / Office / Supervisor)."));

            if (tenantId is not null)
                failures.Add(new("TenantId",
                    "TenantId must be null for Team users — Team membership is modeled via TenantUser, not the per-user binding."));
        }
        else if (userType == UserType.Tenant)
        {
            if (tenantId is null || tenantId == Guid.Empty)
                failures.Add(new("TenantId",
                    "TenantId is required for Tenant users and must be a non-empty GUID matching a Tenant in the master registry."));

            if (teamSubtype is not null)
                failures.Add(new("TeamSubtype",
                    "TeamSubtype must be null for Tenant users — only Team users carry a sub-classification."));

            if (isEnkiAdmin)
                failures.Add(new("IsEnkiAdmin",
                    "Tenant users cannot hold the cross-tenant enki-admin role; clear IsEnkiAdmin first or change UserType."));
        }

        return failures;
    }

    /// <summary>
    /// Convenience overload that lifts the string-typed columns off
    /// <c>ApplicationUser</c> (<c>UserType</c> + <c>TeamSubtype</c> are
    /// stored as SmartEnum names) into typed values, then delegates.
    /// Returns failures for malformed enum names too — protects against
    /// a hand-edited DB row from sneaking past.
    /// </summary>
    public static IReadOnlyList<ValidationFailure> Validate(
        string? userTypeName,
        string? teamSubtypeName,
        Guid?   tenantId,
        bool    isEnkiAdmin)
    {
        UserType? userType = null;
        if (!string.IsNullOrWhiteSpace(userTypeName))
        {
            if (!UserType.TryFromName(userTypeName, out userType))
                return [new("UserType", $"UserType '{userTypeName}' is not a known value.")];
        }

        TeamSubtype? teamSubtype = null;
        if (!string.IsNullOrWhiteSpace(teamSubtypeName))
        {
            if (!TeamSubtype.TryFromName(teamSubtypeName, out teamSubtype))
                return [new("TeamSubtype", $"TeamSubtype '{teamSubtypeName}' is not a known value.")];
        }

        return Validate(userType, teamSubtype, tenantId, isEnkiAdmin);
    }
}
