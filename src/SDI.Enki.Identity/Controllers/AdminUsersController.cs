using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Identity.Concurrency;
using SDI.Enki.Identity.Configuration;
using SDI.Enki.Identity.Data;
using SDI.Enki.Identity.Validation;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.Identity.Controllers;

/// <summary>
/// Cross-tenant user administration endpoints. Backs the Blazor
/// /admin/users pages — list / detail / lock / unlock / toggle admin
/// role / reset password. Auth is by bearer token (issued by this
/// same Identity host) carrying the <c>enki</c> scope and the
/// <c>enki-admin</c> role claim.
///
/// <para>
/// Lives in the Identity host (rather than WebApi) because every
/// action here is a thin shim over <see cref="UserManager{TUser}"/>:
/// keeping this code next to the user store avoids a cross-host hop
/// for admin operations and means WebApi doesn't need to hold an
/// Identity DbContext reference.
/// </para>
///
/// <para>
/// Routes use the EnkiAdmin policy registered in Program.cs. The
/// validation scheme is the OpenIddict.Validation one — same scheme
/// the WebApi uses to validate tokens — set explicitly so the cookie
/// scheme used by the login Razor pages doesn't leak into API calls.
/// </para>
/// </summary>
[ApiController]
[Route("admin/users")]
// Per-action [Authorize(Policy=...)] attributes choose between
// EnkiAdmin (admin only — Team-side ops, capability grants, role
// flips) and EnkiAdminOrOffice (Office can reach the action; the
// per-target helper below tightens for Team targets).
//
// AuthenticationSchemes is pinned on every action attribute (not on
// the class) so the policy evaluator always sees the bearer
// principal. When the scheme sat at the class level and the policy
// at the action level, the action-level attribute fell back to the
// host's default cookie scheme — admins got a 403 on /admin/users
// because the policy saw the (anonymous) cookie principal. The
// EnkiPolicies in Program.cs also pin the scheme to make the policy
// authoritative regardless of attribute structure; this redundant
// pinning is defence-in-depth so a future contributor adding a new
// action without thinking about the scheme can't re-introduce the
// bug.
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class AdminUsersController(
    UserManager<ApplicationUser> userMgr,
    ApplicationDbContext db,
    IOptions<SessionLifetimeOptions> sessionOpts) : ControllerBase
{
    private readonly SessionLifetimeOptions _sessionOpts = sessionOpts.Value;

    // ---------- per-target authority helper ----------

    /// <summary>
    /// Per-action authority check that depends on the TARGET user's
    /// <see cref="UserType"/>. The policy gate (<c>EnkiAdminOrOffice</c>)
    /// only confirms the caller could AT LEAST manage Tenant users; this
    /// inner check tightens to admin-only when the target is Team-type.
    ///
    /// <para>
    /// Returns <c>null</c> when the caller is allowed. Returns a
    /// <c>403 Forbidden</c> ProblemDetails-style result when not.
    /// </para>
    /// </summary>
    private IActionResult? RequireSufficientAuthorityFor(ApplicationUser target)
    {
        var isAdmin = User.IsInRole(AuthConstants.EnkiAdminRole)
                   || User.HasClaim(OpenIddictConstants.Claims.Role, AuthConstants.EnkiAdminRole);
        if (isAdmin) return null;

        if (target.UserType == UserType.Tenant)
            return null;   // Office-or-above (already verified by policy gate) can manage Tenant users.

        return Problem(
            detail:     "Only system administrators may perform this action on a Team-type user.",
            statusCode: StatusCodes.Status403Forbidden,
            title:      "Insufficient authority for this target user");
    }

    /// <summary>
    /// Paginated list of users. <paramref name="skip"/> and
    /// <paramref name="take"/> are clamped — <c>take</c> caps at 500 to
    /// prevent a 100 000-row pull from a stray <c>?take=1000000</c>;
    /// negative values fall back to defaults.
    /// </summary>
    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdmin")]
    [HttpGet]
    public async Task<PagedResult<AdminUserSummaryDto>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 100;
        if (take > 500) take = 500;

        var baseQuery = userMgr.Users.AsNoTracking().OrderBy(u => u.UserName);
        var total     = await baseQuery.CountAsync(ct);

        // Single round-trip — Skip/Take + projection + ToListAsync is
        // one SELECT to the server. Pageable client-side grid (Syncfusion
        // SfGrid in /admin/users) wires straight to the envelope.
        var rows = await baseQuery
            .Skip(skip)
            .Take(take)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.IsEnkiAdmin,
                u.LockoutEnd,
                UserTypeName = u.UserType != null ? u.UserType.Name : null,
                u.TeamSubtype,
                u.TenantId,
            })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var items = rows.Select(u => new AdminUserSummaryDto(
            Id:          u.Id,
            UserName:    u.UserName ?? "",
            Email:       u.Email    ?? "",
            DisplayName: u.UserName ?? "",   // detail endpoint resolves the friendly name
            IsEnkiAdmin: u.IsEnkiAdmin,
            IsLockedOut: u.LockoutEnd is { } end && end > now,
            UserType:    u.UserTypeName,
            TeamSubtype: u.TeamSubtype,
            TenantId:    u.TenantId)).ToList();

        return new PagedResult<AdminUserSummaryDto>(items, total, skip, take);
    }

    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdmin")]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        var claims = await userMgr.GetClaimsAsync(user);
        var firstName   = claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        var lastName    = claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
        var displayName = claims.FirstOrDefault(c => c.Type == "name")?.Value
                          ?? user.UserName
                          ?? "";
        // Surface capability claims (subset of EnkiCapabilities.All) so
        // the admin UI can render the "Special permissions" checkboxes.
        var capabilities = claims
            .Where(c => c.Type == EnkiClaimTypes.Capability)
            .Select(c => c.Value)
            .Where(v => EnkiCapabilities.IsKnown(v))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return Ok(new AdminUserDetailDto(
            Id:                       user.Id,
            UserName:                 user.UserName ?? "",
            Email:                    user.Email    ?? "",
            DisplayName:              displayName,
            FirstName:                firstName,
            LastName:                 lastName,
            IsEnkiAdmin:              user.IsEnkiAdmin,
            IsLockedOut:              user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow,
            LockoutEnd:               user.LockoutEnd,
            AccessFailedCount:        user.AccessFailedCount,
            SessionLifetimeMinutes:   user.SessionLifetimeMinutes,
            SessionLifetimeUpdatedAt: user.SessionLifetimeUpdatedAt,
            SessionLifetimeUpdatedBy: user.SessionLifetimeUpdatedBy,
            UserType:                 user.UserType?.Name,
            TeamSubtype:              user.TeamSubtype,
            TenantId:                 user.TenantId,
            Capabilities:             capabilities,
            ConcurrencyStamp:         user.ConcurrencyStamp ?? ""));
    }

    /// <summary>
    /// Create a new user. Server-generates the initial password and
    /// returns it once in the response — admin reads it off the screen
    /// and hands it out-of-band. Subsequent password changes go through
    /// <c>POST /admin/users/{id}/reset-password</c>.
    ///
    /// <para>
    /// <b>UserType is set here and immutable thereafter.</b> Switching
    /// Team↔Tenant requires a fresh user — the existing user's audit
    /// trail and any membership state stays attached to its original
    /// classification.
    /// </para>
    /// </summary>
    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdminOrOffice")]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserDto dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Classification triplet first — produces clearer field-keyed
        // errors than letting Identity's CreateAsync fail later on a
        // half-built row.
        var validation = UserClassificationValidator.Validate(
            userTypeName:    dto.UserType,
            teamSubtypeName: dto.TeamSubtype,
            tenantId:        dto.TenantId,
            isEnkiAdmin:     false);
        if (validation.Count > 0)
        {
            foreach (var f in validation)
                ModelState.AddModelError(f.Field, f.Message);
            return ValidationProblem(ModelState);
        }

        // Per-target tightening on Create: dto-level rather than entity-
        // level (the user doesn't exist yet). The policy gate
        // (EnkiAdminOrOffice) lets Office through; if the request is
        // creating a Team-type user, demand admin in the inner check.
        if (string.Equals(dto.UserType, UserType.Team.Name, StringComparison.Ordinal))
        {
            var isAdmin = User.IsInRole(AuthConstants.EnkiAdminRole)
                       || User.HasClaim(OpenIddictConstants.Claims.Role, AuthConstants.EnkiAdminRole);
            if (!isAdmin)
                return Problem(
                    detail:     "Only system administrators may provision Team-type users.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title:      "Insufficient authority for this user type");
        }

        // Username uniqueness — Identity returns DuplicateUserName from
        // CreateAsync but the message buries the field. Pre-check so the
        // 400 is field-keyed.
        if (await userMgr.FindByNameAsync(dto.UserName) is not null)
        {
            ModelState.AddModelError(nameof(dto.UserName), $"UserName '{dto.UserName}' is already taken.");
            return ValidationProblem(ModelState);
        }

        // Email uniqueness — same rationale as UserName. Identity's
        // UserValidator catches this when CreateAsync runs (RequireUniqueEmail=true)
        // and the unique index on NormalizedEmail is the DB-level backstop;
        // both surface as messy errors though, so pre-check for the clean
        // field-keyed 400.
        if (await userMgr.FindByEmailAsync(dto.Email) is not null)
        {
            ModelState.AddModelError(nameof(dto.Email), $"Email '{dto.Email}' is already in use by another account.");
            return ValidationProblem(ModelState);
        }

        var user = new ApplicationUser
        {
            Id                 = Guid.NewGuid().ToString(),
            UserName           = dto.UserName,
            NormalizedUserName = dto.UserName.ToUpperInvariant(),
            Email              = dto.Email,
            NormalizedEmail    = dto.Email.ToUpperInvariant(),
            EmailConfirmed     = true,    // pre-MFA / pre-confirm-email phase; revisit in 5b
            LockoutEnabled     = true,
            UserType           = UserType.FromName(dto.UserType!),
            TeamSubtype        = dto.TeamSubtype,
            TenantId           = dto.TenantId,
            SecurityStamp      = Guid.NewGuid().ToString(),
        };

        var temporary = GenerateTemporaryPassword();
        var create = await userMgr.CreateAsync(user, temporary);
        if (!create.Succeeded) return IdentityErrorProblem(create);

        // Profile claims — same shape the seeder uses so the detail
        // endpoint reads the friendly name consistently.
        var claims = new List<System.Security.Claims.Claim>(3)
        {
            new("name", $"{(dto.FirstName ?? "").Trim()} {(dto.LastName ?? "").Trim()}".Trim()),
        };
        if (!string.IsNullOrWhiteSpace(dto.FirstName))
            claims.Add(new("given_name",  dto.FirstName));
        if (!string.IsNullOrWhiteSpace(dto.LastName))
            claims.Add(new("family_name", dto.LastName));
        if (claims.Count > 0)
            await userMgr.AddClaimsAsync(user, claims);

        await WriteAuditAsync(user,
            action:         "UserCreated",
            ct:             ct,
            changedColumns: $"{nameof(ApplicationUser.UserName)}|{nameof(ApplicationUser.Email)}|{nameof(ApplicationUser.UserType)}",
            newValues:      JsonSerializer.Serialize(new
            {
                user.UserName,
                user.Email,
                userType    = user.UserType?.Name,
                user.TeamSubtype,
                user.TenantId,
            }));

        return CreatedAtAction(nameof(Get), new { id = user.Id },
            new CreateUserResponseDto(user.Id, temporary));
    }

    /// <summary>
    /// Update editable profile + classification fields. <b>UserType is
    /// not in the payload</b> — switching Team↔Tenant is forbidden.
    /// Username changes affect the user's login string; the admin UI
    /// shows a confirmation modal. Email changes don't trigger
    /// confirmation today (Phase 5b).
    /// </summary>
    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdminOrOffice")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdateUserDto dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        // Re-validate the (immutable) classification with the new
        // mutable fields. Catches an admin trying to e.g. clear the
        // TeamSubtype on a Team user, or set a Guid.Empty TenantId
        // on a Tenant user. UserType stays whatever the row already
        // carries — the DTO doesn't expose it.
        var validation = UserClassificationValidator.Validate(
            userTypeName:    user.UserType?.Name,
            teamSubtypeName: dto.TeamSubtype,
            tenantId:        dto.TenantId,
            isEnkiAdmin:     user.IsEnkiAdmin);
        if (validation.Count > 0)
        {
            foreach (var f in validation)
                ModelState.AddModelError(f.Field, f.Message);
            return ValidationProblem(ModelState);
        }

        if (this.ApplyClientConcurrencyStamp(db, user, dto.ConcurrencyStamp) is { } badStamp)
            return badStamp;

        // Username collision (a different user already owns the new name).
        if (!string.Equals(user.UserName, dto.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await userMgr.FindByNameAsync(dto.UserName);
            if (existing is not null && existing.Id != user.Id)
            {
                ModelState.AddModelError(nameof(dto.UserName), $"UserName '{dto.UserName}' is already taken.");
                return ValidationProblem(ModelState);
            }
        }

        // Email collision — same shape. The DB-level unique index on
        // NormalizedEmail is the backstop, but a friendly field-keyed 400
        // is better UX than a SqlException from SaveChangesAsync.
        if (!string.Equals(user.Email, dto.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingByEmail = await userMgr.FindByEmailAsync(dto.Email);
            if (existingByEmail is not null && existingByEmail.Id != user.Id)
            {
                ModelState.AddModelError(nameof(dto.Email), $"Email '{dto.Email}' is already in use by another account.");
                return ValidationProblem(ModelState);
            }
        }

        // Snapshot the changed fields for the audit row + decide whether
        // a security-stamp rotation is warranted. Email + name changes
        // don't invalidate tokens; classification + tenant binding do
        // (so a downgraded user gets force-signed-out instead of
        // serving up stale claims until the access token expires).
        var changedColumns = new List<string>();
        var oldSnapshot    = new { user.UserName, user.Email, user.TeamSubtype, user.TenantId };

        if (!string.Equals(user.UserName, dto.UserName, StringComparison.Ordinal))
        {
            user.UserName           = dto.UserName;
            user.NormalizedUserName = dto.UserName.ToUpperInvariant();
            changedColumns.Add(nameof(ApplicationUser.UserName));
        }
        if (!string.Equals(user.Email, dto.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email           = dto.Email;
            user.NormalizedEmail = dto.Email.ToUpperInvariant();
            changedColumns.Add(nameof(ApplicationUser.Email));
        }

        var classificationChanged = false;
        if (!string.Equals(user.TeamSubtype, dto.TeamSubtype, StringComparison.Ordinal))
        {
            user.TeamSubtype = dto.TeamSubtype;
            changedColumns.Add(nameof(ApplicationUser.TeamSubtype));
            classificationChanged = true;
        }
        if (user.TenantId != dto.TenantId)
        {
            user.TenantId = dto.TenantId;
            changedColumns.Add(nameof(ApplicationUser.TenantId));
            classificationChanged = true;
        }

        var update = await userMgr.UpdateAsync(user);
        if (IdentityResultIsConcurrencyFailure(update))
            return ConcurrencyConflict("user");
        if (!update.Succeeded) return IdentityErrorProblem(update);

        // Profile claims (name / given_name / family_name). Diff the
        // existing claim set, drop stale rows, and re-add the new
        // values. Skipping the diff on no-op keeps idempotent calls
        // from churning AspNetUserClaims rows.
        var existingClaims = await userMgr.GetClaimsAsync(user);
        var profileChanged = await SyncProfileClaimAsync(user, existingClaims, "given_name",  dto.FirstName);
        profileChanged    |= await SyncProfileClaimAsync(user, existingClaims, "family_name", dto.LastName);
        var newDisplayName = $"{(dto.FirstName ?? "").Trim()} {(dto.LastName ?? "").Trim()}".Trim();
        profileChanged    |= await SyncProfileClaimAsync(user, existingClaims, "name", newDisplayName);
        if (profileChanged)
            changedColumns.Add("ProfileClaims");

        if (changedColumns.Count == 0)
            return NoContent();   // Idempotent no-op.

        if (classificationChanged)
        {
            // Force-resign so the next exchange re-runs the claims
            // factory under the new classification — otherwise the
            // user keeps minting tokens with stale tenant_id /
            // team_subtype until their refresh window naturally rolls.
            await userMgr.UpdateSecurityStampAsync(user);
        }

        await WriteAuditAsync(user,
            action:         "ProfileEdited",
            ct:             ct,
            changedColumns: string.Join("|", changedColumns),
            oldValues:      JsonSerializer.Serialize(oldSnapshot),
            newValues:      JsonSerializer.Serialize(new
            {
                user.UserName,
                user.Email,
                user.TeamSubtype,
                user.TenantId,
            }));

        return NoContent();
    }

    /// <summary>
    /// Add / update / remove a profile claim (<c>name</c>, <c>given_name</c>,
    /// <c>family_name</c>) so the AspNetUserClaims rows reflect the
    /// edit form. Returns true when the underlying claim set changed.
    /// </summary>
    private async Task<bool> SyncProfileClaimAsync(
        ApplicationUser user,
        IList<System.Security.Claims.Claim> existing,
        string claimType,
        string? newValue)
    {
        var current = existing.FirstOrDefault(c => c.Type == claimType);
        var trimmed = newValue?.Trim();
        var hasNew  = !string.IsNullOrEmpty(trimmed);

        if (current is null && !hasNew) return false;
        if (current?.Value == trimmed) return false;

        if (current is not null)
            await userMgr.RemoveClaimAsync(user, current);

        if (hasNew)
            await userMgr.AddClaimAsync(user, new System.Security.Claims.Claim(claimType, trimmed!));

        return true;
    }

    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdminOrOffice")]
    [HttpPost("{id}/lock")]
    public async Task<IActionResult> Lock(string id, [FromBody] AdminUserActionDto dto, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;
        if (await IsSelf(user))
            return Problem(
                detail:     "Cannot lock your own account.",
                statusCode: StatusCodes.Status409Conflict,
                title:      "Self-lock disallowed");

        if (this.ApplyClientConcurrencyStamp(db, user, dto.ConcurrencyStamp) is { } badStamp)
            return badStamp;

        // Lock for 100 years — effectively indefinite. Admin can unlock
        // via the unlock endpoint at any time.
        var result = await userMgr.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        if (IdentityResultIsConcurrencyFailure(result))
            return ConcurrencyConflict("user");
        if (!result.Succeeded) return IdentityErrorProblem(result);

        await WriteAuditAsync(user, action: "Locked", ct);
        return NoContent();
    }

    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdminOrOffice")]
    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> Unlock(string id, [FromBody] AdminUserActionDto dto, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        if (this.ApplyClientConcurrencyStamp(db, user, dto.ConcurrencyStamp) is { } badStamp)
            return badStamp;

        var result = await userMgr.SetLockoutEndDateAsync(user, null);
        if (IdentityResultIsConcurrencyFailure(result))
            return ConcurrencyConflict("user");
        if (!result.Succeeded) return IdentityErrorProblem(result);
        await userMgr.ResetAccessFailedCountAsync(user);

        await WriteAuditAsync(user, action: "Unlocked", ct);
        return NoContent();
    }

    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdmin")]
    [HttpPost("{id}/admin")]
    public async Task<IActionResult> SetAdminRole(string id, [FromBody] SetAdminRoleDto dto, CancellationToken ct)
    {
        if (!dto.IsAdmin.HasValue)
        {
            ModelState.AddModelError(nameof(dto.IsAdmin), "isAdmin is required.");
            return ValidationProblem(ModelState);
        }
        var desired = dto.IsAdmin.Value;

        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;
        if (await IsSelf(user) && !desired)
            return Problem(
                detail:     "Cannot revoke your own admin role.",
                statusCode: StatusCodes.Status409Conflict,
                title:      "Self-demotion disallowed");

        if (this.ApplyClientConcurrencyStamp(db, user, dto.ConcurrencyStamp) is { } badStamp)
            return badStamp;

        if (user.IsEnkiAdmin == desired)
            return NoContent();

        // Single source of truth — flip the column and rotate the
        // security stamp. EnkiUserClaimsPrincipalFactory derives the
        // role=enki-admin claim from this column on the next sign-in,
        // so there's no AspNetUserClaims write to keep in sync.
        user.IsEnkiAdmin = desired;
        var update = await userMgr.UpdateAsync(user);
        if (IdentityResultIsConcurrencyFailure(update))
            return ConcurrencyConflict("user");
        if (!update.Succeeded) return IdentityErrorProblem(update);

        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user,
            action:         desired ? "RoleGranted" : "RoleRevoked",
            ct,
            changedColumns: nameof(ApplicationUser.IsEnkiAdmin));

        return NoContent();
    }

    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdminOrOffice")]
    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] AdminUserActionDto dto, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        if (this.ApplyClientConcurrencyStamp(db, user, dto.ConcurrencyStamp) is { } badStamp)
            return badStamp;

        // Mint a strong temporary password; admin reads it off the
        // screen and hands it to the user out-of-band until email
        // delivery exists.
        var temporary = GenerateTemporaryPassword();

        var token  = await userMgr.GeneratePasswordResetTokenAsync(user);
        var result = await userMgr.ResetPasswordAsync(user, token, temporary);
        if (IdentityResultIsConcurrencyFailure(result))
            return ConcurrencyConflict("user");
        if (!result.Succeeded) return IdentityErrorProblem(result);

        // Force re-login by rotating the stamp; existing refresh tokens
        // can no longer mint access tokens after this point.
        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user, action: "PasswordReset", ct);
        return Ok(new ResetPasswordResponseDto(temporary));
    }

    /// <summary>
    /// Set or clear a per-user session lifetime override. Pass
    /// <c>SessionLifetimeMinutes = null</c> in the body to clear the
    /// override (revert to the global default); a positive integer
    /// applies that many minutes as the sliding refresh-token window,
    /// clamped to <see cref="SessionLifetimeOptions.MaxRefreshTokenLifetimeMinutes"/>.
    ///
    /// <para>
    /// Like <c>SetAdminRole</c>, this rotates the security stamp so any
    /// already-issued refresh token gets refused on the next exchange and
    /// the new policy takes effect immediately. Without that the old
    /// window stays in force until the prior token's natural expiry.
    /// </para>
    ///
    /// <para>
    /// Self-edit is allowed — Mike (or any admin) can give themselves a
    /// long-lived session. The audit row carries the actor + target
    /// (which match in the self-edit case) so the change is still
    /// attributable.
    /// </para>
    /// </summary>
    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdminOrOffice")]
    [HttpPost("{id}/session-lifetime")]
    public async Task<IActionResult> SetSessionLifetime(
        string id,
        [FromBody] SetSessionLifetimeDto dto,
        CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        if (dto.SessionLifetimeMinutes is int requested)
        {
            if (requested < 1)
            {
                ModelState.AddModelError(
                    nameof(dto.SessionLifetimeMinutes),
                    "SessionLifetimeMinutes must be a positive integer or null to clear the override.");
                return ValidationProblem(ModelState);
            }
            if (requested > _sessionOpts.MaxRefreshTokenLifetimeMinutes)
            {
                ModelState.AddModelError(
                    nameof(dto.SessionLifetimeMinutes),
                    $"SessionLifetimeMinutes must be ≤ {_sessionOpts.MaxRefreshTokenLifetimeMinutes} " +
                    $"(MaxRefreshTokenLifetimeMinutes).");
                return ValidationProblem(ModelState);
            }
        }

        if (this.ApplyClientConcurrencyStamp(db, user, dto.ConcurrencyStamp) is { } badStamp)
            return badStamp;

        // Idempotent: a no-op write is a 204 with no audit row, so the log
        // doesn't fill with "set X to X".
        if (user.SessionLifetimeMinutes == dto.SessionLifetimeMinutes)
            return NoContent();

        var previousMinutes = user.SessionLifetimeMinutes;
        var actor           = userMgr.GetUserName(User) ?? "system";

        user.SessionLifetimeMinutes   = dto.SessionLifetimeMinutes;
        user.SessionLifetimeUpdatedAt = DateTimeOffset.UtcNow;
        user.SessionLifetimeUpdatedBy = actor;

        var update = await userMgr.UpdateAsync(user);
        if (IdentityResultIsConcurrencyFailure(update))
            return ConcurrencyConflict("user");
        if (!update.Succeeded) return IdentityErrorProblem(update);

        // Stamp rotation invalidates any in-flight refresh token issued
        // under the old window — next exchange forces a re-auth under
        // the new policy.
        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user,
            action:         "SessionLifetimeChanged",
            ct:             ct,
            changedColumns: nameof(ApplicationUser.SessionLifetimeMinutes),
            detail:         JsonSerializer.Serialize(new
            {
                previousMinutes,
                newMinutes = dto.SessionLifetimeMinutes,
            }));

        return NoContent();
    }

    // ---------- capability grant / revoke ----------

    /// <summary>
    /// Grant a single capability claim to the user (idempotent —
    /// re-granting an existing claim is a no-op 204). Rotates the
    /// security stamp on a real grant so the new claim takes effect on
    /// the next refresh-token exchange. Audit row records the
    /// capability name in NewValues JSON.
    ///
    /// <para>
    /// Tenant users are rejected by <c>UserClassificationValidator</c> —
    /// capability grants are Team-side only.
    /// </para>
    /// </summary>
    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdmin")]
    [HttpPost("{id}/capabilities/{capability}")]
    public async Task<IActionResult> GrantCapability(
        string id,
        string capability,
        CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        var failures = UserClassificationValidator.ValidateCapabilityGrant(user.UserType, capability);
        if (failures.Count > 0)
        {
            foreach (var f in failures)
                ModelState.AddModelError(f.Field, f.Message);
            return ValidationProblem(ModelState);
        }

        var existing = await userMgr.GetClaimsAsync(user);
        if (existing.Any(c => c.Type == EnkiClaimTypes.Capability && c.Value == capability))
            return NoContent();   // Idempotent.

        var addResult = await userMgr.AddClaimAsync(
            user, new System.Security.Claims.Claim(EnkiClaimTypes.Capability, capability));
        if (!addResult.Succeeded) return IdentityErrorProblem(addResult);

        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user,
            action:         "CapabilityGranted",
            ct:             ct,
            changedColumns: "Capabilities",
            newValues:      JsonSerializer.Serialize(new { capability }));

        return NoContent();
    }

    /// <summary>
    /// Revoke a single capability claim (idempotent — revoking an
    /// absent capability is a no-op 204). Same stamp-rotation +
    /// audit shape as <see cref="GrantCapability"/>.
    /// </summary>
    [Authorize(
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = "EnkiAdmin")]
    [HttpDelete("{id}/capabilities/{capability}")]
    public async Task<IActionResult> RevokeCapability(
        string id,
        string capability,
        CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (RequireSufficientAuthorityFor(user) is { } forbid) return forbid;

        if (!EnkiCapabilities.IsKnown(capability))
        {
            ModelState.AddModelError("capability",
                $"'{capability}' is not a known capability.");
            return ValidationProblem(ModelState);
        }

        var existing = await userMgr.GetClaimsAsync(user);
        var match = existing.FirstOrDefault(c =>
            c.Type == EnkiClaimTypes.Capability && c.Value == capability);
        if (match is null) return NoContent();

        var removeResult = await userMgr.RemoveClaimAsync(user, match);
        if (!removeResult.Succeeded) return IdentityErrorProblem(removeResult);

        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user,
            action:         "CapabilityRevoked",
            ct:             ct,
            changedColumns: "Capabilities",
            newValues:      JsonSerializer.Serialize(new { capability }));

        return NoContent();
    }

    /// <summary>
    /// Append a single <see cref="IdentityAuditLog"/> row for an admin
    /// action against the given user. The actor is the calling admin
    /// (<c>UserManager.GetUserId(User)</c>); the entity-id is the
    /// target user's AspNetUsers Id. We deliberately don't snapshot
    /// the full <c>ApplicationUser</c> JSON here — this audit is for
    /// admin-action attribution, not change-tracking on the user row,
    /// and dumping every field (PasswordHash, SecurityStamp, etc.)
    /// into a JSON column would be unnecessary surface area.
    /// </summary>
    private async Task WriteAuditAsync(
        ApplicationUser user,
        string action,
        CancellationToken ct,
        string? changedColumns = null,
        string? oldValues      = null,
        string? newValues      = null,
        string? detail         = null)
    {
        var actor = userMgr.GetUserId(User) ?? "system";
        // detail is treated as a NewValues payload — IdentityAuditLog has
        // no separate "detail" column. Callers that already populate
        // newValues should pass it explicitly; the alias just keeps the
        // call site readable when there's only one snapshot to record.
        db.IdentityAuditLogs.Add(new IdentityAuditLog
        {
            EntityType     = nameof(ApplicationUser),
            EntityId       = user.Id,
            Action         = action,
            ChangedColumns = changedColumns,
            OldValues      = oldValues,
            NewValues      = newValues ?? detail,
            ChangedAt      = DateTimeOffset.UtcNow,
            ChangedBy      = actor,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// True when the latest <see cref="IdentityResult"/> failed
    /// specifically because the <see cref="ApplicationUser.ConcurrencyStamp"/>
    /// didn't match — i.e. the row moved on under us. ASP.NET Identity
    /// surfaces this as the localised <c>ConcurrencyFailure</c> error
    /// (code <c>"ConcurrencyFailure"</c>); detect by code, not message.
    /// </summary>
    private static bool IdentityResultIsConcurrencyFailure(IdentityResult result) =>
        !result.Succeeded
        && result.Errors.Any(e =>
            string.Equals(e.Code, "ConcurrencyFailure", StringComparison.Ordinal));

    /// <summary>
    /// Translate a concurrency-stamp mismatch into a 409 ProblemDetails
    /// that mirrors the WebApi's <c>SaveOrConflictAsync</c> shape — same
    /// "reload-and-retry" copy so the Blazor admin client renders it
    /// consistently with tenant-side conflict banners.
    /// </summary>
    private IActionResult ConcurrencyConflict(string entityKind) =>
        Problem(
            detail:     $"The {entityKind} was modified by another admin since you loaded it. " +
                        $"Reload to see the latest values, then re-apply your edit.",
            statusCode: StatusCodes.Status409Conflict,
            title:      "Concurrency conflict");

    /// <summary>
    /// Self-protection: an admin can't lock or de-admin their own
    /// account from this UI. Going a step further to require a peer
    /// admin to do it would need a multi-admin invariant we don't
    /// guarantee yet.
    /// </summary>
    private async Task<bool> IsSelf(ApplicationUser target)
    {
        var callerId = userMgr.GetUserId(User);
        if (string.IsNullOrEmpty(callerId)) return false;
        return string.Equals(callerId, target.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// 16 chars from a deduped alphabet, plus one of each required
    /// character class to satisfy the Identity password policy
    /// (digit + non-alpha + upper + lower from <c>Program.cs</c>).
    /// Each character is sampled with <see cref="RandomNumberGenerator.GetInt32(int)"/>
    /// so the distribution is uniform — the previous base64-with-replace
    /// path biased characters that the replace map collapsed onto.
    /// Rejection-shuffle of the policy chars folds them into the body
    /// so the placement isn't a constant tail an attacker can skip.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string lower   = "abcdefghijkmnopqrstuvwxyz";   // no l
        const string upper   = "ABCDEFGHJKLMNPQRSTUVWXYZ";    // no I, O
        const string digits  = "23456789";                    // no 0, 1
        const string symbols = "!@#$%^&*?";
        const string body    = lower + upper + digits + symbols;

        Span<char> buf = stackalloc char[16];
        for (var i = 0; i < buf.Length; i++)
            buf[i] = body[RandomNumberGenerator.GetInt32(body.Length)];

        // Guarantee one char from each policy class — overwrite four
        // distinct positions (chosen without replacement) so the policy
        // chars land at unpredictable indices.
        var positions = ReservoirSample(buf.Length, 4);
        buf[positions[0]] = lower  [RandomNumberGenerator.GetInt32(lower.Length)];
        buf[positions[1]] = upper  [RandomNumberGenerator.GetInt32(upper.Length)];
        buf[positions[2]] = digits [RandomNumberGenerator.GetInt32(digits.Length)];
        buf[positions[3]] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

        return new string(buf);
    }

    private static int[] ReservoirSample(int range, int count)
    {
        // Fisher-Yates partial shuffle — picks `count` distinct ints
        // from [0, range) without allocating the full permutation.
        var pool = new int[range];
        for (var i = 0; i < range; i++) pool[i] = i;
        for (var i = 0; i < count; i++)
        {
            var swapWith = i + RandomNumberGenerator.GetInt32(range - i);
            (pool[i], pool[swapWith]) = (pool[swapWith], pool[i]);
        }
        return pool[..count];
    }

    /// <summary>
    /// Wraps an <see cref="IdentityResult"/> failure as an RFC 7807
    /// ProblemDetails. Keeps the caller's <c>return</c> branches
    /// uniform — every error path on this controller emits the same
    /// content type, no bare anonymous-object payloads.
    /// </summary>
    private ObjectResult IdentityErrorProblem(IdentityResult r) =>
        Problem(
            detail:     string.Join("; ", r.Errors.Select(e => e.Description)),
            statusCode: StatusCodes.Status400BadRequest,
            title:      "Identity operation failed");
}
