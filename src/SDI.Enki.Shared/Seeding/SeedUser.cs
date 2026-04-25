namespace SDI.Enki.Shared.Seeding;

/// <summary>
/// One canonical SDI team-member record. Holds both the Identity-DB
/// <see cref="IdentityId"/> (AspNetUsers.Id) and the master-DB
/// <see cref="MasterUserId"/> (User.Id) so each Guid lives in exactly
/// one place and the two seed paths can't drift.
///
/// <para>
/// The pinning matters because <c>CanAccessTenantHandler</c> compares
/// the OIDC <c>sub</c> claim (which IS the Identity-DB row id) against
/// <c>TenantUser.UserId</c> (which references the master-DB row id) via
/// the master-DB <c>User.IdentityId</c> bridge. If Identity seeds Mike
/// with one Guid and Master seeds him with a different one, his
/// authentication still works but every tenant policy check fails
/// closed — silently. Centralising both Guids here makes that drift
/// impossible.
/// </para>
/// </summary>
public sealed record SeedUser(
    Guid   IdentityId,
    Guid   MasterUserId,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    bool   IsEnkiAdmin = false);
