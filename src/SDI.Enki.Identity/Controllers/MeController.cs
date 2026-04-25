using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
public sealed class MeController(UserManager<ApplicationUser> userMgr) : ControllerBase
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
            return BadRequest(new { error = $"Unknown unit system '{trimmed}'. Expected Field, Metric, or SI." });

        if (user.PreferredUnitSystem != trimmed)
        {
            user.PreferredUnitSystem = trimmed;
            var result = await userMgr.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { errors = result.Errors.Select(e => new { e.Code, e.Description }) });
        }

        return NoContent();
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
