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
public sealed class SystemSettingsController(AthenaMasterDbContext master) : ControllerBase
{
    [HttpGet]
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
public sealed class JobRegionSuggestionsController(AthenaMasterDbContext master) : ControllerBase
{
    [HttpGet]
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
