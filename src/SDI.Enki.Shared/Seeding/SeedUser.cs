namespace SDI.Enki.Shared.Seeding;

/// <summary>
/// One canonical SDI team-member record. Holds both the Identity-DB
/// <see cref="IdentityId"/> (AspNetUsers.Id) and the master-DB
/// <see cref="MasterUserId"/> (User.Id) so each Guid lives in exactly
/// one place and the two seed paths can't drift.
///
/// <para>
/// The pinning matters because the WebApi's <c>TeamAuthHandler</c>
/// compares the OIDC <c>sub</c> claim (which IS the Identity-DB row
/// id) against <c>TenantUser.UserId</c> (which references the master-
/// DB row id) via the master-DB <c>User.IdentityId</c> bridge. If
/// Identity seeds Mike with one Guid and Master seeds him with a
/// different one, his authentication still works but every tenant
/// policy check fails closed — silently. Centralising both Guids
/// here makes that drift impossible.
/// </para>
/// </summary>
public sealed record SeedUser(
    Guid   IdentityId,
    Guid   MasterUserId,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    bool   IsEnkiAdmin = false,
    /// <summary>
    /// Pre-set <c>ApplicationUser.SessionLifetimeMinutes</c> override.
    /// Null = use the global default. Mostly used in dev seed to give
    /// known testers (Mike, Gavin) a long-lived session so #30-style
    /// "I'm always being signed out" doesn't reappear during retests.
    /// Reconciled at every host boot — flipping this changes the row.
    /// </summary>
    int?   SessionLifetimeMinutes = null,
    /// <summary>
    /// Top-level classification — Team or Tenant. Default Team because
    /// every existing seeded user is SDI internal. Tenant seed entries
    /// must also set <see cref="TenantId"/>; Team entries should set
    /// <see cref="TeamSubtype"/>. Validated at seed time by the same
    /// <c>UserClassificationValidator</c> the admin endpoints use, so
    /// an invalid combination fails the host boot rather than silently
    /// landing a malformed row.
    /// </summary>
    string UserType = "Team",
    /// <summary>
    /// Sub-classification for Team users (Field / Office / Supervisor).
    /// Required when <see cref="UserType"/> = Team; must be null for
    /// Tenant. Default Office is a sensible fallback for the existing
    /// roster — adjust per-user in <c>SeedUsers.cs</c>.
    /// </summary>
    string? TeamSubtype = "Office",
    /// <summary>
    /// Hard tenant binding for Tenant users — references
    /// <c>master.Tenants.Id</c>. Required when
    /// <see cref="UserType"/> = Tenant; must be null for Team.
    /// </summary>
    Guid?   TenantId = null,
    /// <summary>
    /// Capability claim values granted at seed time. See
    /// <c>SDI.Enki.Shared.Identity.EnkiCapabilities</c> for the
    /// canonical list. Tenant users must keep this empty (validator
    /// enforces). Default empty.
    /// </summary>
    IReadOnlyList<string>? Capabilities = null);
