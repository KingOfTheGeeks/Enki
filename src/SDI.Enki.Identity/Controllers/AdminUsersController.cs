using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Identity;

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
public sealed class AdminUsersController(UserManager<ApplicationUser> userMgr) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<AdminUserSummaryDto>> List(CancellationToken ct)
    {
        // Read claims via the user-claim store to fill DisplayName.
        // For the list page, a single Users query is enough — claims
        // are cheap on the per-user roundtrip the detail endpoint uses.
        var users = await userMgr.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
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
        return users.Select(u => new AdminUserSummaryDto(
            Id:          u.Id,
            UserName:    u.UserName ?? "",
            Email:       u.Email    ?? "",
            DisplayName: u.UserName ?? "",   // detail endpoint resolves the friendly name
            IsEnkiAdmin: u.IsEnkiAdmin,
            IsLockedOut: u.LockoutEnd is { } end && end > now));
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
        if (await IsSelf(user)) return Conflict(new { error = "Cannot lock your own account." });

        // Lock for 100 years — effectively indefinite. Admin can unlock
        // via the unlock endpoint at any time.
        var result = await userMgr.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        return result.Succeeded ? NoContent() : BadRequest(IdentityErrorPayload(result));
    }

    [HttpPost("{id}/unlock")]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();

        var result = await userMgr.SetLockoutEndDateAsync(user, null);
        if (result.Succeeded) await userMgr.ResetAccessFailedCountAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(IdentityErrorPayload(result));
    }

    [HttpPost("{id}/admin")]
    public async Task<IActionResult> SetAdminRole(string id, [FromBody] SetAdminRoleDto dto, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (await IsSelf(user) && !dto.IsAdmin)
            return Conflict(new { error = "Cannot revoke your own admin role." });

        // Same reconcile path the seeder uses — flip the column, sync
        // the role claim, rotate the security stamp so live refresh
        // tokens stop minting the old claim set.
        if (user.IsEnkiAdmin == dto.IsAdmin)
            return NoContent();

        user.IsEnkiAdmin = dto.IsAdmin;
        var update = await userMgr.UpdateAsync(user);
        if (!update.Succeeded) return BadRequest(IdentityErrorPayload(update));

        var claims  = await userMgr.GetClaimsAsync(user);
        var existing = claims.FirstOrDefault(c =>
            c.Type == "role" && c.Value == AuthConstants.EnkiAdminRole);
        if (dto.IsAdmin && existing is null)
            await userMgr.AddClaimAsync(user, new System.Security.Claims.Claim("role", AuthConstants.EnkiAdminRole));
        else if (!dto.IsAdmin && existing is not null)
            await userMgr.RemoveClaimAsync(user, existing);

        await userMgr.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken ct)
    {
        var user = await userMgr.FindByIdAsync(id);
        if (user is null) return NotFound();

        // Mint a strong temporary password; admin reads it off the
        // screen and hands it to the user out-of-band until email
        // delivery exists. 16 chars of url-safe base64 ≈ 96 bits.
        var temporary = GenerateTemporaryPassword();

        var token  = await userMgr.GeneratePasswordResetTokenAsync(user);
        var result = await userMgr.ResetPasswordAsync(user, token, temporary);
        if (!result.Succeeded) return BadRequest(IdentityErrorPayload(result));

        // Force re-login by rotating the stamp; existing refresh tokens
        // can no longer mint access tokens after this point.
        await userMgr.UpdateSecurityStampAsync(user);

        return Ok(new ResetPasswordResponseDto(temporary));
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

    private static string GenerateTemporaryPassword()
    {
        // 12 random bytes → base64url → 16 chars. Strip padding;
        // append a complexity suffix to satisfy the password policy
        // (digit + non-alpha + upper + lower required by Program.cs).
        var bytes = RandomNumberGenerator.GetBytes(12);
        var alphaNum = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', 'A')
            .Replace('/', 'b');
        return alphaNum + "!9Ax";
    }

    private static object IdentityErrorPayload(IdentityResult r) =>
        new { errors = r.Errors.Select(e => new { e.Code, e.Description }) };
}
