using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Wire shapes for the Identity host's <c>/admin/users/*</c> endpoints.
/// Lightweight DTOs — intentionally a subset of <c>ApplicationUser</c>
/// so a token leak doesn't expose the full Identity row (security
/// stamps, password hashes, etc.).
/// </summary>
public sealed record AdminUserSummaryDto(
    string Id,
    string UserName,
    string Email,
    string DisplayName,
    bool   IsEnkiAdmin,
    bool   IsLockedOut);

public sealed record AdminUserDetailDto(
    string  Id,
    string  UserName,
    string  Email,
    string  DisplayName,
    string? FirstName,
    string? LastName,
    bool    IsEnkiAdmin,
    bool    IsLockedOut,
    DateTimeOffset? LockoutEnd,
    int     AccessFailedCount);

/// <summary>
/// Response from <c>POST /admin/users/{id}/reset-password</c>. The new
/// password is returned ONCE in the body — there's no email pipeline
/// today so the admin reads it off the screen and hands it to the user
/// out-of-band. When email lands, this becomes a 204 instead.
/// </summary>
public sealed record ResetPasswordResponseDto(string TemporaryPassword);

/// <summary>
/// Toggle for the <c>IsEnkiAdmin</c> flag. Idempotent — calling with
/// the same value the user already has is a no-op.
/// </summary>
public sealed record SetAdminRoleDto(
    [Required] bool IsAdmin);
