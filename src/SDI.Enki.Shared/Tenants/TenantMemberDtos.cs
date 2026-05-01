using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tenants;

/// <summary>
/// One row in the tenant's member list. <see cref="RowVersion"/> is the
/// base64-encoded concurrency token round-tripped on PATCH; null only
/// for an unsaved entity (never returned from the list endpoint in
/// practice).
/// </summary>
public sealed record TenantMemberDto(
    Guid   UserId,        // master User.Id
    Guid   IdentityId,    // AspNetUsers.Id (so a future "remove me" UX can match against principal.sub)
    string Username,
    string Role,
    DateTimeOffset GrantedAt,
    string? RowVersion);

/// <summary>
/// Add an existing master User to a tenant. UserId is the master
/// <c>User.Id</c> (not the AspNet id) — the picker resolves users via
/// <c>GET /admin/master-users</c> which returns those Ids.
/// </summary>
public sealed record AddTenantMemberDto(
    [Required] Guid   UserId,
    [Required] string Role);

/// <summary>
/// Change an existing member's role. <see cref="RowVersion"/> is the
/// base64 token returned by the list endpoint; the controller 409s if
/// it doesn't match the current row.
/// </summary>
public sealed record SetTenantMemberRoleDto(
    [Required] string Role,
    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);

/// <summary>
/// One row in the user-picker list backing the Add Member dialog.
/// </summary>
public sealed record MasterUserSummaryDto(
    Guid   UserId,
    Guid   IdentityId,
    string Username);

/// <summary>
/// Idempotent upsert of a master <c>User</c> row from a freshly-created
/// Identity (<c>AspNetUsers</c>) row. Called by the Blazor admin Create
/// flow right after <c>POST /admin/users</c> succeeds for a Team user
/// — Tenant users skip this because they don't participate in the
/// master-DB tenant-membership table.
///
/// <para>
/// Why an explicit sync rather than auto-creation inside the Identity
/// admin endpoint: the Identity host doesn't (and shouldn't) reach
/// across into the master DB. Blazor coordinates the two-call write;
/// if step two fails, the Identity user already exists and the admin
/// can retry the sync without touching Identity.
/// </para>
/// </summary>
public sealed record SyncMasterUserDto(
    [Required] Guid   IdentityId,
    [Required, StringLength(200, MinimumLength = 1)]
    string DisplayName);

/// <summary>
/// Result of <c>POST /admin/master-users/sync</c>.
/// <see cref="Created"/> is true when a new master row was inserted,
/// false when an existing row matched the IdentityId (idempotent path).
/// </summary>
public sealed record SyncMasterUserResponseDto(
    Guid UserId,
    bool Created);
