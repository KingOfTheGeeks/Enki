using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.CalibrationProcessing;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations.Processing;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Wizard backend for calibrating a tool — async upload of 25 shot
/// binaries (0.bin baseline + 1.bin..24.bin) → background parse +
/// NarrowBand → operator picks shots → Marduk compute → Save
/// promotes the result into a persistent <c>Calibration</c> row
/// (auto-superseding the prior current cal).
///
/// All endpoints share the session id returned from the initial upload.
/// Sessions live for 30 minutes idle (sliding) in <c>IMemoryCache</c>;
/// host restart loses in-flight sessions and the operator re-uploads.
/// </summary>
[ApiController]
[Route("tools/{serial:int}/calibrations/process")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed partial class CalibrationProcessingController(
    EnkiMasterDbContext master,
    CalibrationProcessingService processing) : ControllerBase
{
    // ---------- start (multipart upload) ----------

    /// <summary>
    /// Accepts 25 files named <c>0.bin</c> (loop-not-energized baseline)
    /// and <c>1.bin</c>–<c>24.bin</c> (the active shots) and kicks off
    /// the background parse + NarrowBand pass. Returns the session id
    /// so the wizard can poll <c>GET /process/{sessionId}</c> for progress.
    /// </summary>
    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]   // 200 MB total upload
    [RequestSizeLimit(200 * 1024 * 1024)]
    [ProducesResponseType<ProcessingSessionStatusDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(int serial, CancellationToken ct)
    {
        var tool = await master.Tools.FirstOrDefaultAsync(t => t.SerialNumber == serial, ct);
        if (tool is null) return this.NotFoundProblem("Tool", serial.ToString());

        if (tool.Status != ToolStatus.Active)
            return this.ConflictProblem(
                $"Cannot calibrate a tool in status {tool.Status.Name}; reactivate it first.");

        var files = Request.Form.Files;
        if (files.Count != 25)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                ["files"] = [$"Expected exactly 25 binary uploads (0.bin baseline + 1.bin..24.bin), got {files.Count}."],
            });

        var binariesByShot = new Dictionary<int, byte[]>(25);
        foreach (var f in files)
        {
            var match = ShotFileNamePattern().Match(f.FileName);
            if (!match.Success)
                return this.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["files"] = [$"File '{f.FileName}' is not in the expected '{{0..24}}.bin' shape."],
                });

            var index = int.Parse(match.Groups[1].Value);
            if (index is < 0 or > 24)
                return this.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["files"] = [$"Shot index {index} is out of range (must be 0..24)."],
                });
            if (binariesByShot.ContainsKey(index))
                return this.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["files"] = [$"Shot {index}.bin was uploaded twice."],
                });

            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            binariesByShot[index] = ms.ToArray();
        }

        var sessionId = processing.StartSession(tool, binariesByShot);
        var status = processing.GetStatus(sessionId)
            ?? throw new InvalidOperationException("Session vanished immediately after start.");

        return Accepted(
            $"/tools/{serial}/calibrations/process/{sessionId}",
            status);
    }

    // ---------- status poll ----------

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType<ProcessingSessionStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult Status(int serial, Guid sessionId)
    {
        var status = processing.GetStatus(sessionId);
        if (status is null) return this.NotFoundProblem("ProcessingSession", sessionId.ToString());
        if (status.ToolSerial != serial) return this.NotFoundProblem("ProcessingSession", sessionId.ToString());
        return Ok(status);
    }

    // ---------- compute ----------

    [HttpPost("{sessionId:guid}/compute")]
    [ProducesResponseType<ProcessingResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public IActionResult Compute(
        int serial,
        Guid sessionId,
        [FromBody] ProcessingComputeRequestDto request)
    {
        var status = processing.GetStatus(sessionId);
        if (status is null || status.ToolSerial != serial)
            return this.NotFoundProblem("ProcessingSession", sessionId.ToString());

        if (request.CurrentsByShot.Count != 24)
            return this.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(ProcessingComputeRequestDto.CurrentsByShot)] =
                    [$"Expected 24 currents, got {request.CurrentsByShot.Count}."],
            });

        try
        {
            var result = processing.Compute(sessionId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return this.ConflictProblem(ex.Message);
        }
    }

    // ---------- save ----------

    [HttpPost("{sessionId:guid}/save")]
    [ProducesResponseType<ProcessingSaveResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(
        int serial,
        Guid sessionId,
        [FromBody] ProcessingSaveRequestDto request,
        CancellationToken ct)
    {
        var status = processing.GetStatus(sessionId);
        if (status is null || status.ToolSerial != serial)
            return this.NotFoundProblem("ProcessingSession", sessionId.ToString());

        try
        {
            var result = await processing.SaveAsync(master, sessionId, request, ct);
            return CreatedAtAction(
                actionName: nameof(CalibrationsController.Get),
                controllerName: "Calibrations",
                routeValues: new { id = result.CalibrationId },
                value: result);
        }
        catch (InvalidOperationException ex)
        {
            return this.ConflictProblem(ex.Message);
        }
    }

    // Matches "0.bin" through "24.bin" case-insensitive — 0.bin is
    // the loop-not-energized baseline, 1.bin..24.bin are the active
    // shots. Anything else is rejected at the upload boundary before
    // we touch the service.
    [GeneratedRegex(@"^(\d{1,2})\.bin$", RegexOptions.IgnoreCase)]
    private static partial Regex ShotFileNamePattern();
}
