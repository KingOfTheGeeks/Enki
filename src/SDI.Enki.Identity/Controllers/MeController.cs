using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.Identity.Controllers;

/// <summary>
/// Self-service endpoints for the currently signed-in user. Lives on
/// the Identity host because every field exposed here is on
/// <see cref="ApplicationUser"/> — no master-DB join needed.
///
/// <para>
/// Auth is the same OpenIddict-validation scheme the admin endpoints
/// use, but the policy is just <c>EnkiApiScope</c> equivalent (any
/// caller with a valid bearer token of <c>scope=enki</c>) — these are
/// the user's OWN preferences, not someone else's.
/// </para>
/// </summary>
[ApiController]
[Route("me")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class MeController(
    UserManager<ApplicationUser> userMgr,
    ApplicationDbContext db) : ControllerBase
{
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();

        return Ok(new UserPreferencesDto(
            PreferredUnitSystem: user.PreferredUnitSystem));
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> SetPreferences(
        [FromBody] UserPreferencesDto dto,
        CancellationToken ct)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();

        // Validate the unit-system name if supplied. Empty / whitespace
        // is treated as "clear the preference" — explicit null on the
        // wire works too.
        var trimmed = string.IsNullOrWhiteSpace(dto.PreferredUnitSystem)
            ? null
            : dto.PreferredUnitSystem.Trim();

        if (trimmed is not null && !IsKnownUnitSystem(trimmed))
        {
            ModelState.AddModelError(
                nameof(dto.PreferredUnitSystem),
                $"Unknown unit system '{trimmed}'. Expected Field, Metric, or SI.");
            return ValidationProblem(ModelState);
        }

        if (user.PreferredUnitSystem != trimmed)
        {
            user.PreferredUnitSystem = trimmed;
            var result = await userMgr.UpdateAsync(user);
            if (!result.Succeeded)
                return Problem(
                    detail:     string.Join("; ", result.Errors.Select(e => e.Description)),
                    statusCode: StatusCodes.Status400BadRequest,
                    title:      "Identity operation failed");
        }

        return NoContent();
    }

    /// <summary>
    /// Self-service password change. The user supplies their current
    /// password (proving they hold it) plus the new one; ASP.NET Core
    /// Identity enforces the configured policy on the new value and
    /// rotates the security stamp internally on success, which
    /// invalidates any existing refresh tokens. The audit row pairs
    /// with the admin-side <c>PasswordReset</c> entry — the
    /// <c>ChangedBy</c> column distinguishes operator-initiated
    /// changes from admin-initiated ones.
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto dto,
        CancellationToken ct)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();

        // Pre-flight: required-field shape lives in the DTO contract,
        // but the model-validation pipeline doesn't always run on
        // string properties (a missing or empty body deserialises to
        // empty strings rather than null). Reject up front so Identity
        // doesn't see an empty current-password and answer with a
        // misleading "Incorrect password".
        if (string.IsNullOrEmpty(dto.CurrentPassword))
            ModelState.AddModelError(nameof(ChangePasswordDto.CurrentPassword), "Current password is required.");
        if (string.IsNullOrEmpty(dto.NewPassword))
            ModelState.AddModelError(nameof(ChangePasswordDto.NewPassword), "New password is required.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await userMgr.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
            return MapPasswordErrorsToValidationProblem(result);

        // Audit. ChangePasswordAsync already rotates the security stamp
        // internally (UpdateSecurityStampInternal called pre-return), so
        // existing refresh tokens stop minting access tokens after this
        // point — no extra UpdateSecurityStampAsync call needed.
        db.IdentityAuditLogs.Add(new IdentityAuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId   = user.Id,
            Action     = "PasswordChanged",
            ChangedAt  = DateTimeOffset.UtcNow,
            ChangedBy  = userMgr.GetUserId(User) ?? user.Id,
        });
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Maps Identity password-change error codes to per-field validation
    /// errors so the change-password card can render "Current password is
    /// wrong" next to the current-password field and policy violations
    /// next to the new-password field. Unknown codes fold into a generic
    /// model-state error rather than vanishing silently.
    /// </summary>
    private IActionResult MapPasswordErrorsToValidationProblem(IdentityResult result)
    {
        var errors = new ModelStateDictionary();
        foreach (var err in result.Errors)
        {
            var field = err.Code switch
            {
                "PasswordMismatch"               => nameof(ChangePasswordDto.CurrentPassword),
                "PasswordTooShort"               or
                "PasswordRequiresDigit"          or
                "PasswordRequiresUpper"          or
                "PasswordRequiresLower"          or
                "PasswordRequiresNonAlphanumeric" or
                "PasswordRequiresUniqueChars"    => nameof(ChangePasswordDto.NewPassword),
                _                                => string.Empty,
            };
            errors.AddModelError(field, err.Description);
        }
        return ValidationProblem(errors);
    }

    private async Task<ApplicationUser?> CurrentUser()
    {
        var sub = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub)) return null;
        return await userMgr.FindByIdAsync(sub);
    }

    /// <summary>
    /// Inline allowlist — keeps Identity from taking a project ref on
    /// SDI.Enki.Core just to import the SmartEnum. The shared
    /// <c>UnitSystem</c> values are stable; if a new preset is added
    /// the validator gets updated alongside it.
    /// </summary>
    private static bool IsKnownUnitSystem(string s) =>
        s is "Field" or "Metric" or "SI";
}
