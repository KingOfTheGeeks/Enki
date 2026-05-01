using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Licensing;
using SDI.Enki.Core.Master.Licensing.Enums;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Licensing;
using SDI.Enki.Shared.Licensing;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-level License registry. <c>EnkiAdminOnly</c> across the board —
/// generating a license is privileged because it bakes specific tools +
/// calibrations + features into a signed envelope that any field-deployed
/// Marduk decoder will trust. Re-download serves the original bytes from
/// the <see cref="License.FileBytes"/> column. Revocation is a soft
/// status flip + reason; it doesn't try to invalidate already-issued
/// files in the field.
/// </summary>
[ApiController]
[Route("licenses")]
[Authorize(Policy = EnkiPolicies.CanManageLicensing)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class LicensesController(
    EnkiMasterDbContext master,
    ILicenseFileGenerator generator) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<LicenseSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<LicenseSummaryDto>> List(CancellationToken ct)
    {
        var rows = await master.Licenses
            .AsNoTracking()
            .OrderByDescending(l => l.IssuedAt)
            .Select(l => new
            {
                l.Id, l.LicenseKey, l.Licensee, l.IssuedAt, l.ExpiresAt,
                StatusName = l.Status.Name,
                l.ToolSnapshotJson, l.CalibrationSnapshotJson, l.CreatedAt,
            })
            .ToListAsync(ct);

        return rows.Select(l =>
        {
            var (toolCount, calCount) = CountSnapshots(l.ToolSnapshotJson, l.CalibrationSnapshotJson);
            return new LicenseSummaryDto(
                l.Id, l.LicenseKey, l.Licensee,
                l.IssuedAt, l.ExpiresAt,
                l.StatusName, toolCount, calCount, l.CreatedAt);
        });
    }

    // ---------- detail ----------

    [HttpGet("{id:guid}")]
    [ProducesResponseType<LicenseDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await master.Licenses
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new
            {
                l.Id, l.LicenseKey, l.Licensee, l.IssuedAt, l.ExpiresAt,
                StatusName = l.Status.Name,
                l.FeaturesJson, l.ToolSnapshotJson, l.CalibrationSnapshotJson,
                FileSizeBytes = l.FileBytes.Length,
                l.RevokedReason, l.RevokedAt, l.CreatedAt, l.CreatedBy,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("License", id.ToString());

        var features = JsonSerializer.Deserialize<LicenseFeaturesDto>(row.FeaturesJson, JsonOptions)
            ?? new LicenseFeaturesDto();
        var tools = JsonSerializer.Deserialize<List<LicenseToolSnapshotDto>>(row.ToolSnapshotJson, JsonOptions)
            ?? [];
        var cals  = JsonSerializer.Deserialize<List<LicenseCalibrationSnapshotDto>>(row.CalibrationSnapshotJson, JsonOptions)
            ?? [];

        return Ok(new LicenseDetailDto(
            row.Id, row.LicenseKey, row.Licensee,
            row.IssuedAt, row.ExpiresAt, row.StatusName,
            features, tools, cals,
            row.FileSizeBytes,
            row.RevokedReason, row.RevokedAt, row.CreatedAt, row.CreatedBy));
    }

    // ---------- file download ----------

    /// <summary>
    /// Streams the original encrypted <c>.lic</c> bytes back. Re-issue
    /// works by serving the same column on subsequent requests; the
    /// generator never re-runs (which would re-randomize salt/nonce
    /// and produce different bytes for the same conceptual license).
    /// </summary>
    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken ct)
    {
        var row = await master.Licenses
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.Licensee, l.IssuedAt, l.FileBytes })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("License", id.ToString());

        var safeName = MakeSafeFileName($"{row.Licensee}-{row.IssuedAt:yyyy-MM-dd}.lic");
        return File(row.FileBytes, "application/octet-stream", safeName);
    }

    // ---------- sidecar key file download ----------

    /// <summary>
    /// Plain-text sidecar with Licensee / Expiry / License Key — the
    /// customer needs this to know what to type in to activate the .lic.
    /// Same shape as Nabu's <c>GenerateKeyFileContent</c>.
    /// </summary>
    [HttpGet("{id:guid}/key")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadKeyFile(Guid id, CancellationToken ct)
    {
        var row = await master.Licenses
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.Licensee, l.ExpiresAt, l.LicenseKey })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("License", id.ToString());

        var content =
            $"Licensee: {row.Licensee}\n" +
            $"Expiry: {row.ExpiresAt:yyyy-MM-dd}\n" +
            $"License Key (GUID): {row.LicenseKey:D}\n";

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var safeName = MakeSafeFileName($"{row.Licensee}-{row.ExpiresAt:yyyy-MM-dd}-key.txt");
        return File(bytes, "text/plain; charset=utf-8", safeName);
    }

    // ---------- create / generate ----------

    [HttpPost]
    [ProducesResponseType<LicenseDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLicenseDto dto,
        CancellationToken ct)
    {
        // Operator-supplied license key must be a real GUID; the customer
        // types this in to activate, so empty Guid is meaningless.
        if (dto.LicenseKey == Guid.Empty)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateLicenseDto.LicenseKey)] = ["License key must be a non-empty GUID."],
            });

        // Domain rule: AllowWarriorLogging requires AllowWarrior. Mirror
        // Nabu's UX coupling so a wizard misconfiguration can't ship an
        // invalid feature combination via direct API call.
        if (dto.Features.AllowWarriorLogging && !dto.Features.AllowWarrior)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(LicenseFeaturesDto.AllowWarriorLogging)] =
                    ["AllowWarriorLogging requires AllowWarrior to also be on."],
            });

        // Pre-check uniqueness so we can return a clean 409 instead of a
        // raw DbUpdateException from the unique index violation.
        if (await master.Licenses.AnyAsync(l => l.LicenseKey == dto.LicenseKey, ct))
            return this.ConflictProblem(
                $"License key {dto.LicenseKey} is already in use; pick another.");

        var toolIds = dto.ToolIds.Distinct().ToList();
        if (toolIds.Count == 0)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateLicenseDto.ToolIds)] = ["At least one tool id is required."],
            });

        var tools = await master.Tools
            .AsNoTracking()
            .Where(t => toolIds.Contains(t.Id))
            .ToListAsync(ct);

        if (tools.Count != toolIds.Count)
        {
            var missing = toolIds.Except(tools.Select(t => t.Id)).ToList();
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateLicenseDto.ToolIds)] = [$"Unknown tool id(s): {string.Join(", ", missing)}"],
            });
        }

        // Resolve the calibration to bake in for each tool: explicit override
        // if the caller supplied one, otherwise the tool's current cal
        // (newest non-superseded, non-nominal preferred but any current is OK).
        var calibrationsByToolId = new Dictionary<Guid, Calibration>(tools.Count);
        var overrides = dto.CalibrationOverridesByToolId ?? new Dictionary<Guid, Guid>();

        foreach (var tool in tools)
        {
            Calibration? cal;
            if (overrides.TryGetValue(tool.Id, out var explicitCalId))
            {
                cal = await master.Calibrations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == explicitCalId && c.ToolId == tool.Id, ct);

                if (cal is null)
                    return this.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(CreateLicenseDto.CalibrationOverridesByToolId)] =
                            [$"Calibration {explicitCalId} not found for tool {tool.SerialNumber}."],
                    });
            }
            else
            {
                cal = await master.Calibrations
                    .AsNoTracking()
                    .Where(c => c.ToolId == tool.Id && !c.IsSuperseded)
                    .OrderByDescending(c => c.CalibrationDate)
                    .FirstOrDefaultAsync(ct);

                if (cal is null)
                    return this.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(CreateLicenseDto.ToolIds)] =
                            [$"Tool {tool.SerialNumber} has no current calibration to bake in."],
                    });
            }

            calibrationsByToolId[tool.Id] = cal;
        }

        // Ahead-of-DB-write: generate the envelope. Crypto failures throw
        // and the global exception handler turns them into a 500 with a
        // clean ProblemDetails — generation is rare enough that we'd
        // rather see the stack than swallow.
        var fileBytes = generator.Generate(
            licensee:             dto.Licensee,
            licenseKey:           dto.LicenseKey,
            expiry:               dto.ExpiresAt,
            tools:                tools,
            calibrationsByToolId: calibrationsByToolId,
            features:             dto.Features);

        var nowUtc = DateTimeOffset.UtcNow;

        var toolSnapshots = tools.Select(t => new LicenseToolSnapshotDto(
            t.Id, t.SerialNumber, t.FirmwareVersion, t.MagnetometerCount, t.AccelerometerCount));
        var calSnapshots = tools.Select(t =>
        {
            var c = calibrationsByToolId[t.Id];
            return new LicenseCalibrationSnapshotDto(
                c.Id, c.ToolId, c.SerialNumber,
                NameForSnapshot(c, t),
                c.CalibrationDate, c.CalibratedBy);
        });

        var entity = new License(dto.Licensee, dto.LicenseKey, nowUtc, new DateTimeOffset(dto.ExpiresAt.ToUniversalTime(), TimeSpan.Zero))
        {
            FeaturesJson            = JsonSerializer.Serialize(dto.Features, JsonOptions),
            ToolSnapshotJson        = JsonSerializer.Serialize(toolSnapshots, JsonOptions),
            CalibrationSnapshotJson = JsonSerializer.Serialize(calSnapshots, JsonOptions),
            FileBytes               = fileBytes,
        };

        master.Licenses.Add(entity);
        await master.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(Get),
            new { id = entity.Id },
            new LicenseDetailDto(
                entity.Id, entity.LicenseKey, entity.Licensee,
                entity.IssuedAt, entity.ExpiresAt,
                entity.Status.Name,
                dto.Features,
                toolSnapshots.ToList(),
                calSnapshots.ToList(),
                entity.FileBytes.Length,
                RevokedReason: null, RevokedAt: null,
                entity.CreatedAt, entity.CreatedBy));
    }

    // ---------- revoke ----------

    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(
        Guid id,
        [FromBody] RevokeLicenseDto dto,
        CancellationToken ct)
    {
        var license = await master.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (license is null) return this.NotFoundProblem("License", id.ToString());

        if (license.Status == LicenseStatus.Revoked)
            return NoContent();   // Idempotent.

        license.Status        = LicenseStatus.Revoked;
        license.RevokedAt     = DateTimeOffset.UtcNow;
        license.RevokedReason = dto.Reason;

        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- helpers ----------

    private static (int toolCount, int calCount) CountSnapshots(string toolJson, string calJson)
    {
        try
        {
            using var toolDoc = JsonDocument.Parse(toolJson);
            using var calDoc  = JsonDocument.Parse(calJson);
            var tools = toolDoc.RootElement.ValueKind == JsonValueKind.Array ? toolDoc.RootElement.GetArrayLength() : 0;
            var cals  = calDoc.RootElement.ValueKind  == JsonValueKind.Array ? calDoc.RootElement.GetArrayLength()  : 0;
            return (tools, cals);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static string NameForSnapshot(Calibration c, Tool tool)
    {
        // The Calibration entity doesn't have a Name column; the Marduk
        // payload does, but parsing it just for the snapshot is overkill.
        // Use a stable readable label: "{generation}-{serial}-{date}".
        return $"{tool.Generation.Name}-{tool.SerialNumber}-{c.CalibrationDate:yyyy-MM-dd}";
    }

    private static string MakeSafeFileName(string raw)
    {
        var sanitized = string.Concat(raw.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return sanitized;
    }
}
