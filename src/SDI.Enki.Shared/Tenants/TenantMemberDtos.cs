using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tenants;

/// <summary>
/// One row in the tenant's member list.
/// </summary>
public sealed record TenantMemberDto(
    Guid   UserId,        // master User.Id
    Guid   IdentityId,    // AspNetUsers.Id (so a future "remove me" UX can match against principal.sub)
    string Username,
    string Role,
    DateTimeOffset GrantedAt);

/// <summary>
/// Add an existing master User to a tenant. UserId is the master
/// <c>User.Id</c> (not the AspNet id) — the picker resolves users via
/// <c>GET /admin/master-users</c> which returns those Ids.
/// </summary>
public sealed record AddTenantMemberDto(
    [Required] Guid   UserId,
    [Required] string Role);

/// <summary>
/// Change an existing member's role.
/// </summary>
public sealed record SetTenantMemberRoleDto(
    [Required] string Role);

/// <summary>
/// One row in the user-picker list backing the Add Member dialog.
/// </summary>
public sealed record MasterUserSummaryDto(
    Guid   UserId,
    Guid   IdentityId,
    string Username);
