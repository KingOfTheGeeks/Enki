using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Identity.Data;
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
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = "EnkiAdmin")]
public sealed class AdminUsersController(
    UserManager<ApplicationUser> userMgr,
    ApplicationDbContext db) : ControllerBase
{
    /// <summary>
    /// Paginated list of users. <paramref name="skip"/> and
    /// <paramref name="take"/> are clamped — <c>take</c> caps at 500 to
    /// prevent a 100 000-row pull from a stray <c>?take=1000000</c>;
    /// negative values fall back to defaults.
    /// </summary>
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
            })
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var items = rows.Select(u => new AdminUserSummaryDto(
            Id:          u.Id,
            UserName:    u.UserName ?? "",
            Email:       u.Email    ?? "",
            DisplayName: u.UserName ?? "",   // detail endpoint resolves the friendly name
            IsEnkiAdmin: u.IsEnkiAdmin,
            IsLockedOut: u.LockoutEnd is { } end && end > now)).ToList();

        return new PagedResult<AdminUserSummaryDto>(items, total, skip, take);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();

        var claims = await userMgr.GetClaimsAsync(user);
        var firstName   = claims.FirstOrDefault(c => c.Type == "given_name")?.Value;
        var lastName    = claims.FirstOrDefault(c => c.Type == "family_name")?.Value;
        var displayName = claims.FirstOrDefault(c => c.Type == "name")?.Value
                          ?? user.UserName
                          ?? "";

        return Ok(new AdminUserDetailDto(
            Id:                user.Id,
            UserName:          user.UserName ?? "",
            Email:             user.Email    ?? "",
            DisplayName:       displayName,
            FirstName:         firstName,
            LastName:          lastName,
            IsEnkiAdmin:       user.IsEnkiAdmin,
            IsLockedOut:       user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow,
            LockoutEnd:        user.LockoutEnd,
            AccessFailedCount: user.AccessFailedCount));
    }

    [HttpPost("{id}/lock")]
    public async Task<IActionResult> Lock(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (await IsSelf(user))
            return Problem(
                detail:     "Cannot lock your own account.",
                statusCode: StatusCodes.Status409Conflict,
                title:      "Self-lock disallowed");

        // Lock for 100 years — effectively indefinite. Admin can unlock
        // via the unlock endpoint at any time.
        var result = await userMgr.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        if (!result.Succeeded) return IdentityErrorProblem(result);

        await WriteAuditAsync(user, action: "Locked", ct);
        return NoContent();
    }

    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();

        var result = await userMgr.SetLockoutEndDateAsync(user, null);
        if (!result.Succeeded) return IdentityErrorProblem(result);
        await userMgr.ResetAccessFailedCountAsync(user);

        await WriteAuditAsync(user, action: "Unlocked", ct);
        return NoContent();
    }

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
        if (await IsSelf(user) && !desired)
            return Problem(
                detail:     "Cannot revoke your own admin role.",
                statusCode: StatusCodes.Status409Conflict,
                title:      "Self-demotion disallowed");

        if (user.IsEnkiAdmin == desired)
            return NoContent();

        // Single source of truth — flip the column and rotate the
        // security stamp. EnkiUserClaimsPrincipalFactory derives the
        // role=enki-admin claim from this column on the next sign-in,
        // so there's no AspNetUserClaims write to keep in sync.
        user.IsEnkiAdmin = desired;
        var update = await userMgr.UpdateAsync(user);
        if (!update.Succeeded) return IdentityErrorProblem(update);

        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user,
            action:         desired ? "RoleGranted" : "RoleRevoked",
            ct,
            changedColumns: nameof(ApplicationUser.IsEnkiAdmin));

        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();

        // Mint a strong temporary password; admin reads it off the
        // screen and hands it to the user out-of-band until email
        // delivery exists.
        var temporary = GenerateTemporaryPassword();

        var token  = await userMgr.GeneratePasswordResetTokenAsync(user);
        var result = await userMgr.ResetPasswordAsync(user, token, temporary);
        if (!result.Succeeded) return IdentityErrorProblem(result);

        // Force re-login by rotating the stamp; existing refresh tokens
        // can no longer mint access tokens after this point.
        await userMgr.UpdateSecurityStampAsync(user);

        await WriteAuditAsync(user, action: "PasswordReset", ct);
        return Ok(new ResetPasswordResponseDto(temporary));
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
        string? changedColumns = null)
    {
        var actor = userMgr.GetUserId(User) ?? "system";
        db.IdentityAuditLogs.Add(new IdentityAuditLog
        {
            EntityType     = nameof(ApplicationUser),
            EntityId       = user.Id,
            Action         = action,
            ChangedColumns = changedColumns,
            ChangedAt      = DateTimeOffset.UtcNow,
            ChangedBy      = actor,
        });
        await db.SaveChangesAsync(ct);
    }

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
