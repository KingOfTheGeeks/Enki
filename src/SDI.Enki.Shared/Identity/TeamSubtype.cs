using Ardalis.SmartEnum;

namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Sub-classification on <see cref="UserType.Team"/> users — encodes
/// the SDI-internal role the team member fills. Required when
/// <c>ApplicationUser.UserType == Team</c>; must be null when
/// <c>UserType == Tenant</c> (validated centrally by
/// <c>UserClassificationValidator</c>, enforced in the admin endpoints
/// and the seed reconciler).
///
/// <para>
/// Stored on <c>ApplicationUser.TeamSubtype</c> as the SmartEnum's name
/// (<c>"Field"</c> / <c>"Office"</c> / <c>"Supervisor"</c>) so the column
/// is human-readable in DB tools without a join. Don't renumber once
/// shipped — int values are written through the value converter on
/// <see cref="ApplicationDbContext"/>.
/// </para>
///
/// <para>
/// Lives in <c>SDI.Enki.Shared.Identity</c> alongside <see cref="UserType"/>:
/// the Identity host writes / reads the column, the WebApi reads it off
/// the access token to make authorization decisions, and the Blazor
/// admin UI binds it to a dropdown.
/// </para>
/// </summary>
public sealed class TeamSubtype : SmartEnum<TeamSubtype>
{
    /// <summary>
    /// Field engineer — operates downhole tools at the rig site.
    /// Typically the largest cohort by headcount.
    /// </summary>
    public static readonly TeamSubtype Field      = new(nameof(Field),      1);

    /// <summary>Office staff — non-field role (analysts, coordinators, support).</summary>
    public static readonly TeamSubtype Office     = new(nameof(Office),     2);

    /// <summary>
    /// Field supervisor — coordinates one or more Field engineers.
    /// Distinct from Office because supervisors retain field-rotation
    /// access patterns even when they don't physically run tools.
    /// </summary>
    public static readonly TeamSubtype Supervisor = new(nameof(Supervisor), 3);

    private TeamSubtype(string name, int value) : base(name, value) { }
}
