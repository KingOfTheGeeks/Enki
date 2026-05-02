using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Settings;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Settings;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// App-wide settings managed at runtime. Tightly scoped today: only the
/// keys listed in <see cref="SystemSettingKeys"/> are accepted. Both
/// reads and writes require the <c>enki-admin</c> role (some future
/// settings will be sensitive). Specific settings that non-admin users
/// need (e.g. <c>Jobs:RegionSuggestions</c> for the region picker) get
/// their own narrowly-scoped public endpoint.
///
/// <para>
/// Auth gate is at the controller level on
/// <see cref="EnkiPolicies.EnkiAdminOnly"/> — the policy itself
/// enforces the role + scope, no inline checks. Same shape every
/// admin endpoint should use.
/// </para>
/// </summary>
[ApiController]
[Route("admin/settings")]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class SystemSettingsController(EnkiMasterDbContext master) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<SystemSettingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        // Materialize known keys so the admin UI shows every key even
        // if the row hasn't been created yet. Missing keys come back
        // with empty values; the UI can present them as "unset".
        var rows = await master.SystemSettings
            .AsNoTracking()
            .Where(s => SystemSettingKeys.All.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, ct);

        var result = SystemSettingKeys.All
            .Select(key => rows.TryGetValue(key, out var s)
                ? new SystemSettingDto(key, s.Value, s.UpdatedAt, s.UpdatedBy)
                : new SystemSettingDto(key, "", null, null))
            .ToList();

        return Ok(result);
    }

    [HttpPut("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Set(
        string key,
        [FromBody] SetSystemSettingDto dto,
        CancellationToken ct)
    {
        if (!SystemSettingKeys.IsKnown(key))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["key"] = [$"Unknown setting key '{key}'."],
            });

        // Per-key value validation — rejects numbers outside the ranges the
        // Compute DTO enforces, non-numeric input where a number is required,
        // unknown enum values, etc. Without this, an admin can save e.g.
        // DipDegrees = 200 here and get a confusing 400 from the Compute
        // endpoint mid-wizard (issue #43).
        var error = ValidateValue(key, dto.Value);
        if (error is not null)
            return Problem(
                detail:     error,
                statusCode: StatusCodes.Status400BadRequest,
                title:      "Invalid setting value");

        var existing = await master.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            master.SystemSettings.Add(new SystemSetting(key, dto.Value));
        }
        else
        {
            existing.Value = dto.Value;
        }
        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Returns a human-readable error message when <paramref name="value"/>
    /// is invalid for <paramref name="key"/>; null when the value is fine.
    /// Numeric calibration defaults are bounded by the same ranges the
    /// <c>ProcessingComputeRequestDto</c> data annotations enforce — saving
    /// a value here that would later fail Compute validation is the bug
    /// this guards against.
    /// </summary>
    private static string? ValidateValue(string key, string value) => key switch
    {
        SystemSettingKeys.CalibrationDefaultGTotal             => RequireDouble(value, min: 0d,    max: double.MaxValue, label: "GTotal"),
        SystemSettingKeys.CalibrationDefaultBTotal             => RequireDouble(value, min: 0d,    max: double.MaxValue, label: "BTotal"),
        SystemSettingKeys.CalibrationDefaultDipDegrees         => RequireDouble(value, min: -180d, max: 180d,            label: "Dip"),
        SystemSettingKeys.CalibrationDefaultDeclinationDegrees => RequireDouble(value, min: -180d, max: 180d,            label: "Declination"),
        SystemSettingKeys.CalibrationDefaultCoilConstant       => RequireDouble(value, min: 0d,    max: double.MaxValue, label: "Coil constant"),
        SystemSettingKeys.CalibrationDefaultActiveBDipDegrees  => RequireDouble(value, min: -180d, max: 180d,            label: "Active B dip"),
        SystemSettingKeys.CalibrationDefaultSampleRateHz       => RequireDouble(value, min: 0.001, max: 100_000d,        label: "Sample rate"),
        SystemSettingKeys.CalibrationDefaultManualSign         => RequireDouble(value, min: -1d,   max: 1d,              label: "Manual sign"),
        SystemSettingKeys.CalibrationDefaultCurrent            => RequireDouble(value, min: 0d,    max: double.MaxValue, label: "Default current"),
        SystemSettingKeys.CalibrationDefaultMagSource          => RequireOneOf(value, ["static", "active"], label: "Mag source"),
        SystemSettingKeys.CalibrationDefaultIncludeDeclination => RequireBool(value, label: "Include declination"),
        // Free-form text settings (e.g. JobRegionSuggestions) — accept anything.
        _ => null,
    };

    private static string? RequireDouble(string raw, double min, double max, string label)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return $"{label} is required.";
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return $"{label} must be a number; got '{raw}'.";
        if (v < min || v > max)
            return $"{label} must be between {FormatBound(min)} and {FormatBound(max)}; got {v.ToString(CultureInfo.InvariantCulture)}.";
        return null;
    }

    private static string? RequireOneOf(string raw, string[] allowed, string label)
    {
        var trimmed = raw.Trim();
        if (allowed.Any(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase)))
            return null;
        return $"{label} must be one of: {string.Join(", ", allowed)}. Got '{raw}'.";
    }

    private static string? RequireBool(string raw, string label)
    {
        if (bool.TryParse(raw.Trim(), out _)) return null;
        return $"{label} must be 'true' or 'false'; got '{raw}'.";
    }

    private static string FormatBound(double v) =>
        v == double.MaxValue ? "∞" : v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Public-to-authenticated read of one specific setting: the region
/// suggestions list used by the Job create / edit pages. Lives in a
/// separate controller so the auth surface stays narrow — admin
/// endpoints are admin-only, this one is "any caller with a valid
/// token". Returns the seeded list if the row hasn't been customised.
/// </summary>
[ApiController]
[Route("jobs/region-suggestions")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class JobRegionSuggestionsController(EnkiMasterDbContext master) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<RegionSuggestionsDto>(StatusCodes.Status200OK)]
    public async Task<RegionSuggestionsDto> Get(CancellationToken ct)
    {
        var row = await master.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == SystemSettingKeys.JobRegionSuggestions, ct);

        var raw = row?.Value ?? "";
        var regions = raw
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new RegionSuggestionsDto(regions);
    }
}
