using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Tools;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-level Tool registry endpoints. Tools are fleet-wide — a tool
/// serves many tenants across its lifetime, so this controller is not
/// tenant-scoped. Anyone with a valid enki-scope token can read; mutating
/// endpoints (Phase 2) will tighten to <c>EnkiAdminOnly</c>.
///
/// Routes use the tool's <c>SerialNumber</c> rather than the GUID Id —
/// operators know the serial, that's what they'll type in the URL.
/// </summary>
[ApiController]
[Route("tools")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class ToolsController(
    EnkiMasterDbContext master,
    ICurrentUser currentUser) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<ToolSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<ToolSummaryDto>> List(
        [FromQuery] string? status,
        [FromQuery] string? generation,
        CancellationToken ct)
    {
        var rows = await master.Tools
            .AsNoTracking()
            .OrderBy(t => t.SerialNumber)
            .Select(t => new
            {
                t.Id,
                t.SerialNumber,
                t.FirmwareVersion,
                GenerationName = t.Generation.Name,
                StatusName = t.Status.Name,
                t.MagnetometerCount,
                t.AccelerometerCount,
                CalibrationCount = t.Calibrations.Count,
                LatestCalibrationDate = (DateTimeOffset?)t.Calibrations
                    .OrderByDescending(c => c.CalibrationDate)
                    .Select(c => c.CalibrationDate)
                    .FirstOrDefault(),
                t.CreatedAt,
                t.RowVersion,
            })
            .ToListAsync(ct);

        var filtered = rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
            filtered = filtered.Where(r => string.Equals(r.StatusName, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(generation))
            filtered = filtered.Where(r => string.Equals(r.GenerationName, generation, StringComparison.OrdinalIgnoreCase));

        return filtered.Select(t => new ToolSummaryDto(
            t.Id,
            t.SerialNumber,
            ToolDisplay.Name(t.GenerationName, t.SerialNumber),
            t.FirmwareVersion,
            t.GenerationName,
            t.StatusName,
            t.MagnetometerCount,
            t.AccelerometerCount,
            t.CalibrationCount,
            t.LatestCalibrationDate == default ? null : t.LatestCalibrationDate,
            t.CreatedAt,
            ConcurrencyHelper.EncodeRowVersion(t.RowVersion)));
    }

    // ---------- detail ----------

    [HttpGet("{serial:int}")]
    [ProducesResponseType<ToolDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int serial, CancellationToken ct)
    {
        var row = await master.Tools
            .AsNoTracking()
            .Where(t => t.SerialNumber == serial)
            .Select(t => new
            {
                t.Id,
                t.SerialNumber,
                t.FirmwareVersion,
                GenerationName = t.Generation.Name,
                StatusName = t.Status.Name,
                t.Configuration,
                t.Size,
                t.MagnetometerCount,
                t.AccelerometerCount,
                t.Notes,
                CalibrationCount = t.Calibrations.Count,
                LatestCalibrationDate = (DateTimeOffset?)t.Calibrations
                    .OrderByDescending(c => c.CalibrationDate)
                    .Select(c => c.CalibrationDate)
                    .FirstOrDefault(),
                t.CreatedAt,
                t.UpdatedAt,
                t.RowVersion,

                // Retirement metadata + replacement-tool denormalisation.
                // Joining via Select-on-nav keeps this a single round-trip.
                DispositionName     = t.Disposition == null ? null : t.Disposition.Name,
                t.RetiredAt,
                t.RetiredBy,
                t.RetirementReason,
                t.RetirementLocation,
                ReplacementSerial      = (int?)(t.ReplacementTool == null ? null : (int?)t.ReplacementTool.SerialNumber),
                ReplacementGenerationName = t.ReplacementTool == null ? null : t.ReplacementTool.Generation.Name,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return this.NotFoundProblem("Tool", serial.ToString());

        var replacementDisplay = row.ReplacementSerial is { } rs && row.ReplacementGenerationName is { } rg
            ? ToolDisplay.Name(rg, rs)
            : null;

        return Ok(new ToolDetailDto(
            row.Id,
            row.SerialNumber,
            ToolDisplay.Name(row.GenerationName, row.SerialNumber),
            row.FirmwareVersion,
            row.GenerationName,
            row.StatusName,
            row.Configuration,
            row.Size,
            row.MagnetometerCount,
            row.AccelerometerCount,
            row.Notes,
            row.CalibrationCount,
            row.LatestCalibrationDate == default ? null : row.LatestCalibrationDate,
            row.CreatedAt,
            row.UpdatedAt,
            ConcurrencyHelper.EncodeRowVersion(row.RowVersion),
            row.DispositionName,
            row.RetiredAt,
            row.RetiredBy,
            row.RetirementReason,
            row.RetirementLocation,
            row.ReplacementSerial,
            replacementDisplay));
    }

    // ---------- create ----------

    [Authorize(Policy = EnkiPolicies.CanManageMasterTools)]
    [HttpPost]
    [ProducesResponseType<ToolDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateToolDto dto,
        CancellationToken ct)
    {
        if (await master.Tools.AnyAsync(t => t.SerialNumber == dto.SerialNumber, ct))
            return this.ConflictProblem(
                $"A tool with serial {dto.SerialNumber} already exists.");

        var generation = ResolveGeneration(dto.Generation, dto.FirmwareVersion, dto.Configuration, dto.Size);
        if (generation is null)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateToolDto.Generation)] = [GenerationErrorMessage(dto.Generation)],
            });

        var tool = new Tool(dto.SerialNumber, dto.FirmwareVersion, dto.MagnetometerCount, dto.AccelerometerCount)
        {
            Configuration = dto.Configuration,
            Size          = dto.Size,
            Generation    = generation,
            Status        = ToolStatus.Active,
            Notes         = dto.Notes,
        };

        master.Tools.Add(tool);
        await master.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { serial = tool.SerialNumber },
            new ToolDetailDto(
                tool.Id, tool.SerialNumber,
                ToolDisplay.Name(tool.Generation.Name, tool.SerialNumber),
                tool.FirmwareVersion, tool.Generation.Name, tool.Status.Name,
                tool.Configuration, tool.Size, tool.MagnetometerCount, tool.AccelerometerCount,
                tool.Notes, CalibrationCount: 0, LatestCalibrationDate: null,
                tool.CreatedAt, tool.UpdatedAt,
                ConcurrencyHelper.EncodeRowVersion(tool.RowVersion),
                Disposition: null, RetiredAt: null, RetiredBy: null,
                RetirementReason: null, RetirementLocation: null,
                ReplacementToolSerial: null, ReplacementToolDisplayName: null));
    }

    // ---------- update ----------

    [Authorize(Policy = EnkiPolicies.CanManageMasterTools)]
    [HttpPut("{serial:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        int serial,
        [FromBody] UpdateToolDto dto,
        CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (this.ApplyClientRowVersion(master, tool, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (!ToolGeneration.TryFromName(dto.Generation, ignoreCase: true, out var generation))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(UpdateToolDto.Generation)] = [GenerationErrorMessage(dto.Generation)],
            });

        // Serial rename: refurb sometimes re-issues the serial. Validate the
        // unique-index collision up front so we return a clean 409 instead
        // of an opaque DbUpdateException from SaveChangesAsync.
        if (dto.SerialNumber != tool.SerialNumber)
        {
            var collision = await master.Tools.AnyAsync(
                t => t.SerialNumber == dto.SerialNumber && t.Id != tool.Id, ct);
            if (collision)
                return this.ConflictProblem(
                    $"Cannot rename to serial {dto.SerialNumber}; another tool already uses it.");
        }

        tool.SerialNumber       = dto.SerialNumber;
        tool.FirmwareVersion    = dto.FirmwareVersion;
        tool.Generation         = generation;
        tool.Configuration      = dto.Configuration;
        tool.Size               = dto.Size;
        tool.MagnetometerCount  = dto.MagnetometerCount;
        tool.AccelerometerCount = dto.AccelerometerCount;
        tool.Notes              = dto.Notes;
        // UpdatedAt + UpdatedBy are stamped by the DbContext audit
        // interceptor — don't set them manually.

        if (await master.SaveOrConflictAsync(this, "Tool", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // Resolves a Generation: explicit name if provided & valid, else infer
    // from firmware + config + size. Returns null only when an explicit name
    // was supplied AND it doesn't match any SmartEnum value — the caller
    // turns that into a 400 ValidationProblem.
    private static ToolGeneration? ResolveGeneration(string? explicitName, string firmware, int configuration, int size)
    {
        if (string.IsNullOrWhiteSpace(explicitName))
            return Tool.InferGeneration(firmware, configuration, size);

        return ToolGeneration.TryFromName(explicitName, ignoreCase: true, out var g) ? g : null;
    }

    private static string GenerationErrorMessage(string? supplied) =>
        $"Unknown generation '{supplied}'. Allowed: {string.Join(", ", ToolGeneration.List.Select(g => g.Name))}.";

    // ---------- retire ----------

    [Authorize(Policy = EnkiPolicies.CanManageMasterTools)]
    [HttpPost("{serial:int}/retire")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retire(
        int serial,
        [FromBody] RetireToolDto dto,
        CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (this.ApplyClientRowVersion(master, tool, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        // Disposition: round-tripped by name. Unknown name → 400 with the
        // allowed values so the UI can surface a precise field error.
        if (!ToolDisposition.TryFromName(dto.Disposition, ignoreCase: true, out var disposition))
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(RetireToolDto.Disposition)] = [DispositionErrorMessage(dto.Disposition)],
            });

        // Replacement-tool resolution. We persist the FK by Id, but the
        // operator types a serial — resolve here and reject up front so the
        // 400 is field-keyed rather than an opaque DbUpdateException.
        Guid? replacementToolId = null;
        if (dto.ReplacementToolSerial is { } replacementSerial)
        {
            if (replacementSerial == tool.SerialNumber)
                return this.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(RetireToolDto.ReplacementToolSerial)] = ["A tool cannot replace itself."],
                });

            replacementToolId = await master.Tools
                .Where(t => t.SerialNumber == replacementSerial)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);

            if (replacementToolId is null)
                return this.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(RetireToolDto.ReplacementToolSerial)] = [$"No tool exists with serial {replacementSerial}."],
                });
        }

        var newStatus = disposition == ToolDisposition.Lost ? ToolStatus.Lost : ToolStatus.Retired;
        var effectiveAt = new DateTimeOffset(dto.EffectiveDate!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var stamper = currentUser.UserName ?? currentUser.UserId ?? "system";

        // Idempotency: a re-retire with all the same fields is a 204 no-op.
        // Re-retiring with different fields (operator amending — Retired
        // upgraded to Sold, or fixing the reason text) updates and bumps
        // the RowVersion through normal SaveOrConflictAsync.
        if (tool.Status == newStatus
            && Equals(tool.Disposition, disposition)
            && tool.RetiredAt == effectiveAt
            && tool.RetirementReason == dto.Reason
            && tool.RetirementLocation == dto.FinalLocation
            && tool.ReplacementToolId == replacementToolId)
        {
            return NoContent();
        }

        tool.Status             = newStatus;
        tool.Disposition        = disposition;
        tool.RetiredAt          = effectiveAt;
        tool.RetiredBy          = stamper;
        tool.RetirementReason   = dto.Reason;
        tool.RetirementLocation = dto.FinalLocation;
        tool.ReplacementToolId  = replacementToolId;

        if (await master.SaveOrConflictAsync(this, "Tool", ct) is { } conflict)
            return conflict;
        return NoContent();
    }

    // ---------- reactivate ----------

    [Authorize(Policy = EnkiPolicies.CanManageMasterTools)]
    [HttpPost("{serial:int}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(
        int serial,
        [FromBody] LifecycleTransitionDto dto,
        CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (this.ApplyClientRowVersion(master, tool, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (tool.Status == ToolStatus.Active)
            return NoContent();   // Idempotent.

        // Reactivating clears every retirement column — the tool is back in
        // service and shouldn't carry a stale "sold to vendor X" line. The
        // master audit log preserves the prior values, so the historical
        // disposition isn't lost.
        tool.Status             = ToolStatus.Active;
        tool.Disposition        = null;
        tool.RetiredAt          = null;
        tool.RetiredBy          = null;
        tool.RetirementReason   = null;
        tool.RetirementLocation = null;
        tool.ReplacementToolId  = null;

        if (await master.SaveOrConflictAsync(this, "Tool", ct) is { } conflict)
            return conflict;
        return NoContent();
    }

    private static string DispositionErrorMessage(string? supplied) =>
        $"Unknown disposition '{supplied}'. Allowed: {string.Join(", ", ToolDisposition.List.Select(d => d.Name))}.";

    // ---------- nested calibrations ----------

    [HttpGet("{serial:int}/calibrations")]
    [ProducesResponseType<IEnumerable<CalibrationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCalibrations(int serial, CancellationToken ct)
    {
        var tool = await master.Tools
            .AsNoTracking()
            .Where(t => t.SerialNumber == serial)
            .Select(t => new { t.Id, GenerationName = t.Generation.Name })
            .FirstOrDefaultAsync(ct);

        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        var displayName = ToolDisplay.Name(tool.GenerationName, serial);

        var rows = await master.Calibrations
            .AsNoTracking()
            .Where(c => c.ToolId == tool.Id)
            .OrderByDescending(c => c.CalibrationDate)
            .Select(c => new CalibrationSummaryDto(
                c.Id,
                c.ToolId,
                c.SerialNumber,
                displayName,
                c.CalibrationDate,
                c.CalibratedBy,
                c.MagnetometerCount,
                c.IsNominal,
                c.IsSuperseded,
                c.Source.Name))
            .ToListAsync(ct);

        return Ok(rows);
    }
}
