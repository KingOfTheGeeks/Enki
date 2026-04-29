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
