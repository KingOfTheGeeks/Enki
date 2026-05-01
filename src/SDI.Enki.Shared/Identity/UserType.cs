using Ardalis.SmartEnum;

namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Top-level discriminator on <c>ApplicationUser.UserType</c>. Two
/// buckets: SDI-internal (<see cref="Team"/>) and external customer
/// (<see cref="Tenant"/>). The bucket is chosen at user-creation time
/// and is <b>immutable</b> afterwards — switching a user from Team to
/// Tenant (or vice versa) means creating a fresh account, not mutating
/// the column.
///
/// <para>
/// Persisted via the value converter on <c>ApplicationDbContext</c> as
/// the SmartEnum's name (<c>"Team"</c> / <c>"Tenant"</c>) so the column
/// is human-readable in DB tools. Don't renumber existing values once
/// shipped — int values flow through the converter for any legacy
/// reader still on integer storage.
/// </para>
///
/// <para>
/// Pairs with two complementary columns enforced via
/// <c>UserClassificationValidator</c>:
/// <list type="bullet">
///   <item>Team users carry a non-null <see cref="TeamSubtype"/>
///   (Field / Office / Supervisor) and can hold <c>IsEnkiAdmin</c> +
///   any number of <c>TenantUser</c> memberships.</item>
///   <item>Tenant users carry a non-null <c>TenantId</c> binding them
///   to <b>exactly one</b> tenant; they can never be <c>IsEnkiAdmin</c>
///   and don't appear in the <c>TenantUser</c> membership table.</item>
/// </list>
/// </para>
/// </summary>
public sealed class UserType : SmartEnum<UserType>
{
    /// <summary>SDI internal employee account.</summary>
    public static readonly UserType Team = new(nameof(Team), 1);

    /// <summary>
    /// External customer user, hard-bound to one tenant via
    /// <c>ApplicationUser.TenantId</c>. Cannot hold <c>enki-admin</c>
    /// and cannot reach master-registry endpoints.
    /// </summary>
    public static readonly UserType Tenant = new(nameof(Tenant), 2);

    private UserType(string name, int value) : base(name, value) { }
}
