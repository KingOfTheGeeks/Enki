using Ardalis.SmartEnum;

namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Discriminator on <c>ApplicationUser.UserType</c>. Replaces the
/// legacy free-string column with a SmartEnum so a typo in seed data
/// or a future admin endpoint can't silently store a value no reader
/// understands.
///
/// <para>
/// Lives in <c>SDI.Enki.Shared.Identity</c> rather than
/// <c>SDI.Enki.Core</c> because the Identity host references Shared
/// but deliberately avoids a Core ref (see
/// <c>docs/ArchDecisions.md</c> §4).
/// </para>
///
/// <para>
/// Int values are persisted via the value converter on
/// <c>ApplicationDbContext</c>; do not renumber once shipped.
/// </para>
/// </summary>
public sealed class UserType : SmartEnum<UserType>
{
    /// <summary>SDI internal employee account.</summary>
    public static readonly UserType Team = new(nameof(Team), 1);

    /// <summary>
    /// External tenant user — placeholder. Comes online when external
    /// invites + tenant-scoped signup ship; today every account is
    /// <see cref="Team"/>.
    /// </summary>
    public static readonly UserType External = new(nameof(External), 2);

    private UserType(string name, int value) : base(name, value) { }
}
