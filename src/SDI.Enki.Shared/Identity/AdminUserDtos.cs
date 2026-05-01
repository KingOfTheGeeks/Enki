using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Wire shapes for the Identity host's <c>/admin/users/*</c> endpoints.
/// Lightweight DTOs — intentionally a subset of <c>ApplicationUser</c>
/// so a token leak doesn't expose the full Identity row (security
/// stamps, password hashes, etc.).
/// </summary>
public sealed record AdminUserSummaryDto(
    string  Id,
    string  UserName,
    string  Email,
    string  DisplayName,
    bool    IsEnkiAdmin,
    bool    IsLockedOut,
    /// <summary>Team / Tenant — see <c>UserType</c> SmartEnum.</summary>
    string? UserType,
    /// <summary>Field / Office / Supervisor for Team users; null otherwise.</summary>
    string? TeamSubtype,
    /// <summary>Bound tenant Id for Tenant users; null for Team users.</summary>
    Guid?   TenantId);

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
    int     AccessFailedCount,
    /// <summary>
    /// Per-user refresh-token lifetime override in minutes; null = use
    /// the global default. Set via <c>POST /admin/users/{id}/session-lifetime</c>.
    /// See <c>ApplicationUser.SessionLifetimeMinutes</c> for the full contract.
    /// </summary>
    int? SessionLifetimeMinutes,
    DateTimeOffset? SessionLifetimeUpdatedAt,
    string?         SessionLifetimeUpdatedBy,
    /// <summary>Top-level classification — Team or Tenant. Immutable after creation.</summary>
    string? UserType,
    /// <summary>Field / Office / Supervisor for Team users; null for Tenant.</summary>
    string? TeamSubtype,
    /// <summary>Bound tenant Id for Tenant users; null for Team.</summary>
    Guid?   TenantId,
    /// <summary>
    /// ASP.NET Identity's optimistic-concurrency token — a string GUID
    /// rotated on every save. Round-tripped through every mutation
    /// (Lock / Unlock / SetAdminRole / ResetPassword) so concurrent
    /// admin edits to the same user surface as 409 instead of last-
    /// writer-wins. See
    /// <c>SDI.Enki.Identity.Concurrency.IdentityConcurrencyHelper</c>.
    /// </summary>
    string  ConcurrencyStamp);

/// <summary>
/// Body for <c>POST /admin/users</c> (creation). Server-generates the
/// initial password and returns it once in <see cref="CreateUserResponseDto"/> —
/// admin reads it off the screen and hands it to the user out-of-band.
/// Same model as <c>POST /admin/users/{id}/reset-password</c>.
///
/// <para>
/// Classification triplet (UserType / TeamSubtype / TenantId) is
/// validated against <c>UserClassificationValidator</c>; an invalid
/// combination is a 400 with field-keyed errors. UserType is
/// <b>immutable after creation</b> — changing it later requires
/// creating a fresh account, not editing this row.
/// </para>
/// </summary>
public sealed record CreateUserDto(
    [Required, StringLength(256, MinimumLength = 1)]
    string  UserName,

    [Required, EmailAddress, StringLength(256)]
    string  Email,

    [StringLength(100)]
    string? FirstName,

    [StringLength(100)]
    string? LastName,

    /// <summary>"Team" or "Tenant".</summary>
    [Required]
    string? UserType,

    /// <summary>Required when UserType == Team. Field / Office / Supervisor.</summary>
    string? TeamSubtype,

    /// <summary>Required when UserType == Tenant. Empty Guid is rejected.</summary>
    Guid?   TenantId);

/// <summary>
/// One-shot return for <c>POST /admin/users</c>. <see cref="TemporaryPassword"/>
/// is shown to the admin once; the controller's response is the only
/// place it appears (no DB write of the plaintext) so the operator
/// must hand it out-of-band before navigating away.
/// </summary>
public sealed record CreateUserResponseDto(
    string Id,
    string TemporaryPassword);

/// <summary>
/// Body for <c>PUT /admin/users/{id}</c>. Updates the editable profile
/// fields + the mutable classification fields. <c>UserType</c> is
/// deliberately absent — switching Team↔Tenant is forbidden and
/// requires creating a new user (preserves invariants on existing
/// memberships and audit history).
///
/// <para>
/// <b>Username editability</b> is allowed (the OIDC <c>sub</c> is the
/// immutable Identity GUID, not the UserName) but the admin UI warns
/// that this changes the user's login string — communicate out-of-band
/// before saving.
/// </para>
/// </summary>
public sealed record UpdateUserDto(
    [Required, StringLength(256, MinimumLength = 1)]
    string  UserName,

    [Required, EmailAddress, StringLength(256)]
    string  Email,

    [StringLength(100)]
    string? FirstName,

    [StringLength(100)]
    string? LastName,

    /// <summary>Editable for Team users (Field / Office / Supervisor); ignored for Tenant.</summary>
    string? TeamSubtype,

    /// <summary>Editable for Tenant users (move between tenants); ignored for Team.</summary>
    Guid?   TenantId,

    [Required(ErrorMessage = "ConcurrencyStamp is required for optimistic concurrency.")]
    string? ConcurrencyStamp);

/// <summary>
/// Body for <c>POST /admin/users/{id}/session-lifetime</c>. Pass
/// <see cref="SessionLifetimeMinutes"/> = <c>null</c> to clear the
/// per-user override (revert to the global default). Positive integers
/// set a sliding refresh-token lifetime; the controller clamps to
/// <c>SessionLifetimeOptions.MaxRefreshTokenLifetimeMinutes</c>.
/// </summary>
public sealed record SetSessionLifetimeDto(
    int? SessionLifetimeMinutes,
    [Required(ErrorMessage = "ConcurrencyStamp is required for optimistic concurrency.")]
    string? ConcurrencyStamp = null);

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
///
/// <para>
/// <c>IsAdmin</c> is nullable on the wire (<see cref="Required"/> doesn't
/// fire on a non-nullable <c>bool</c> — its default-zero value would
/// satisfy the attribute and quietly send <c>false</c>). The controller
/// rejects the request when <c>HasValue</c> is false so a missing JSON
/// field can't accidentally revoke admin.
/// </para>
/// </summary>
public sealed record SetAdminRoleDto(
    [Required] bool? IsAdmin,
    [Required(ErrorMessage = "ConcurrencyStamp is required for optimistic concurrency.")]
    string? ConcurrencyStamp = null);

/// <summary>
/// Body shape for the parameterless admin actions (Lock, Unlock,
/// ResetPassword) — they take no payload of their own, just the
/// caller's last-seen <see cref="AdminUserDetailDto.ConcurrencyStamp"/>
/// so the same optimistic-concurrency check applies as for SetAdminRole.
/// </summary>
public sealed record AdminUserActionDto(
    [Required(ErrorMessage = "ConcurrencyStamp is required for optimistic concurrency.")]
    string? ConcurrencyStamp = null);

/// <summary>
/// Self-service user preferences. Backs the <c>/account/settings</c>
/// Blazor page; read/written through Identity's <c>/me/preferences</c>
/// endpoints. Unset values mean "fall back to whatever default the
/// consumer uses".
/// </summary>
public sealed record UserPreferencesDto(
    string? PreferredUnitSystem);
