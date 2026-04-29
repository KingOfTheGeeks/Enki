using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using AMR.Core.Calibration.Computation.Implementations;
using AMR.Core.Calibration.Computation.Infrastructure;
using AMR.Core.Calibration.Computation.Models;
using AMR.Core.Calibration.Models;
using AMR.Core.Models;
using AMR.Core.Services;
using AMR.Core.Telemetry.Domain.Contracts;
using AMR.Core.Telemetry.Domain.Models;
using AMR.Core.Telemetry.Domain.Options;
using AMR.Core.Telemetry.Infrastructure.Warrior.Bootstrap;
using AMR.Core.Telemetry.Infrastructure.Warrior.IO;
using AMR.Core.Telemetry.Infrastructure.Warrior.Registry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Shared.Calibrations.Processing;
using FirmwareVersion = AMR.Core.Models.FirmwareVersion;

namespace SDI.Enki.Infrastructure.CalibrationProcessing;

/// <summary>
/// Async backend for the ToolCalibrate.razor wizard. Mirrors Nabu's
/// <c>CalibrationCreationService</c> pipeline (Warrior parse →
/// <c>NarrowBandShotProcessor</c> → <c>CalibrationComputationService</c>)
/// but reshaped for the web context — operators upload 24 binaries via
/// HTTP, the parse + NarrowBand pass runs on a background <see cref="Task"/>,
/// the wizard polls a status endpoint, and the final Save writes a
/// Calibration row plus the original binaries (zipped) into the master DB.
///
/// Sessions are held in <see cref="IMemoryCache"/> with a 30-minute sliding
/// expiry. Restart of the host loses in-flight sessions — operator
/// re-uploads. That's intentional for v1; persisting partial sessions
/// would force a separate table for a workflow that completes in under
/// a minute most of the time.
///
/// Marduk's <c>WarriorFrameRegistry</c> is built once at construction
/// (singleton lifetime); the registry is thread-safe for reads, which is
/// what the parallel parse needs.
/// </summary>
public sealed class CalibrationProcessingService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CalibrationProcessingService> _logger;
    private readonly IWarriorFrameRegistry _frameRegistry;

    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public CalibrationProcessingService(IMemoryCache cache, ILogger<CalibrationProcessingService> logger)
    {
        _cache = cache;
        _logger = logger;

        var registry = new WarriorFrameRegistry();
        WarriorParserBootstrapper.RegisterAllParsers(registry, [typeof(WarriorBinaryReader).Assembly]);
        _frameRegistry = registry;
    }

    /// <summary>
    /// Creates a new processing session, kicks off the background parse
    /// + NarrowBand pass, and returns the session id. The wizard then
    /// polls <see cref="GetStatus"/> until <see cref="ProcessingSessionState.ReadyForCompute"/>.
    /// </summary>
    /// <param name="tool">Resolved Tool from master DB.</param>
    /// <param name="binariesByShot">
    /// 25 raw binary uploads keyed by 0..24 — index 0 is the
    /// <c>0.bin</c> baseline (loop not energized), 1..24 are the active
    /// shots that Marduk's compute consumes. Caller has already validated
    /// the count + names.
    /// </param>
    public Guid StartSession(Tool tool, IReadOnlyDictionary<int, byte[]> binariesByShot)
    {
        if (binariesByShot.Count != 25)
            throw new ArgumentException($"Expected exactly 25 binaries (0.bin baseline + 1..24), got {binariesByShot.Count}.", nameof(binariesByShot));
        for (int i = 0; i <= 24; i++)
            if (!binariesByShot.ContainsKey(i))
                throw new ArgumentException($"Missing shot {i}.bin.", nameof(binariesByShot));

        var sessionId = Guid.CreateVersion7();
        var session = new Session(sessionId, tool, binariesByShot);
        Store(session);

        // Background fire-and-forget. Captures session by reference; updates
        // mutable fields under the session lock and re-stores into the cache
        // so the sliding expiry resets on every progress write.
        _ = Task.Run(() => RunParsePassAsync(session));

        return sessionId;
    }

    public ProcessingSessionStatusDto? GetStatus(Guid sessionId)
    {
        var session = TryGet(sessionId);
        if (session is null) return null;

        // Renew sliding expiry on every poll so a wizard that sits open
        // doesn't lose its session.
        Store(session);

        lock (session.Lock)
        {
            return new ProcessingSessionStatusDto(
                SessionId:           session.Id,
                ToolSerial:          session.Tool.SerialNumber,
                State:               session.State.ToString(),
                ShotsParsed:         session.ParsedShots.Count(s => s is not null),
                Previews:            session.Previews.Where(p => p is not null).Cast<ProcessingShotPreviewDto>().ToList(),
                Result:              session.Result,
                SavedCalibrationId:  session.SavedCalibrationId,
                Error:               session.Error);
        }
    }

    /// <summary>
    /// Runs Marduk's <c>CalibrationComputationService.Compute</c> against
    /// the parsed shots with the operator's selections. Synchronous — the
    /// compute itself is fast (under 1s for typical inputs); the wizard
    /// can wait on the response. Sets session state to Computed (or
    /// Failed) and parks the result on the session.
    /// </summary>
    public ProcessingResultDto Compute(Guid sessionId, ProcessingComputeRequestDto request)
    {
        var session = TryGet(sessionId)
            ?? throw new InvalidOperationException("Session not found or expired.");

        lock (session.Lock)
        {
            if (session.State != ProcessingSessionState.ReadyForCompute &&
                session.State != ProcessingSessionState.Computed)
            {
                throw new InvalidOperationException(
                    $"Session is in state {session.State}; compute is only allowed when ReadyForCompute or Computed.");
            }
            session.State = ProcessingSessionState.Computing;
        }

        try
        {
            var (gpm, bpm, bLoc) = GetToolGeometry(session.Tool.MagnetometerCount, session.Tool.Generation.Name);

            // Marduk's CalibrationComputationRequest.Shots is fixed-length 24
            // (active shots only). Index 0 of session.ParsedShots is the
            // baseline (0.bin, loop not energized) — slice it off here.
            // 1-based indices the operator sends already line up with Marduk.
            var activeShots = new CalibrationShotData[24];
            for (int i = 1; i <= 24; i++)
                activeShots[i - 1] = session.ParsedShots[i]
                    ?? throw new InvalidOperationException($"Shot {i} not parsed.");

            var enabled = request.EnabledShotIndices.Distinct().Where(i => i is >= 1 and <= 24).ToArray();
            if (enabled.Length == 0)
                throw new InvalidOperationException("At least one active shot must be enabled.");
            var axialAlign = enabled.Where(i => i <= 8).ToArray();

            var marduk = new CalibrationComputationService();
            var mardukRequest = new CalibrationComputationRequest
            {
                Name                  = $"{session.Tool.SerialNumber}-{DateTime.UtcNow:yyyy-MM-dd}",
                CalibrationDate       = DateTime.UtcNow,
                GTotal                = request.GTotal,
                BTotal                = request.BTotal,
                DipDegrees            = request.DipDegrees,
                DeclinationDegrees    = request.DeclinationDegrees,
                CoilConstant          = request.CoilConstant,
                ActiveBDipDegrees     = request.ActiveBDipDegrees,
                MagnetometerCount     = session.Tool.MagnetometerCount,
                Gpm                   = gpm,
                Bpm                   = bpm,
                BLoc                  = bLoc,
                SampleRateHz          = request.SampleRateHz,
                Shots                 = activeShots,
                UseShotIndices        = enabled,
                AxialAlignShotIndices = axialAlign,
                ManualSigns           = Enumerable.Repeat(request.ManualSign, 24).ToArray(),
                Currents              = request.CurrentsByShot.ToArray(),
                MtfoChoice            = 0,
            };

            var mardukResult = marduk.Compute(mardukRequest);

            var diagnostics = BuildDiagnostics(mardukResult, enabled);

            var resultDto = new ProcessingResultDto(
                Success:           mardukResult.Success,
                GravityResidual:   mardukResult.GravityResidual,
                MagneticResiduals: mardukResult.MagneticResiduals?.ToList() ?? [],
                Report:            mardukResult.Report?.ToList() ?? [],
                ShotDiagnostics:   diagnostics,
                Error:             mardukResult.Success ? null : "Marduk compute returned Success=false; see Report.");

            lock (session.Lock)
            {
                session.LastComputeRequest = request;
                session.LastMardukResult   = mardukResult;
                session.Result             = resultDto;
                session.State              = mardukResult.Success
                    ? ProcessingSessionState.Computed
                    : ProcessingSessionState.ReadyForCompute;  // Allow retry with different selections.
            }
            Store(session);

            return resultDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Calibration compute failed for session {SessionId}", sessionId);
            lock (session.Lock)
            {
                session.State = ProcessingSessionState.Failed;
                session.Error = $"Compute failed: {ex.Message}";
            }
            Store(session);
            throw;
        }
    }

    /// <summary>
    /// Promotes the computed result into a persistent <see cref="Calibration"/>
    /// row. Caller has already shown the user a confirm-before-save warning;
    /// this method auto-flips the previous current cal for the same tool to
    /// Superseded inside the same <see cref="EnkiMasterDbContext"/> change
    /// set. Session moves to <see cref="ProcessingSessionState.Saved"/>;
    /// further saves are rejected (one cal per session).
    /// </summary>
    public async Task<ProcessingSaveResultDto> SaveAsync(
        Data.EnkiMasterDbContext db,
        Guid sessionId,
        ProcessingSaveRequestDto request,
        CancellationToken ct)
    {
        var session = TryGet(sessionId)
            ?? throw new InvalidOperationException("Session not found or expired.");

        CalibrationComputationResult mardukResult;
        Tool tool;
        IReadOnlyDictionary<int, byte[]> binaries;
        CalibrationShotData[] parsedShots;

        lock (session.Lock)
        {
            if (session.State == ProcessingSessionState.Saved && session.SavedCalibrationId is { } existing)
            {
                // Idempotent — repeated Save returns the same calibration id.
                return new ProcessingSaveResultDto(existing, session.SupersededCalibrationId);
            }
            if (session.State != ProcessingSessionState.Computed || session.LastMardukResult is null)
                throw new InvalidOperationException(
                    $"Session is in state {session.State}; Save is only allowed when Computed.");

            mardukResult = session.LastMardukResult;
            tool         = session.Tool;
            binaries     = session.RawBinaries;
            parsedShots  = session.ParsedShots
                .Select((s, i) => s ?? throw new InvalidOperationException($"Shot {i + 1} missing."))
                .ToArray();
            session.State = ProcessingSessionState.Saved;  // optimistic; rolled back below on failure.
        }

        try
        {
            var nowUtc = DateTime.UtcNow;
            var nMags = mardukResult.Bpm.Length;

            // Marduk → Nabu/Enki ToolCalibration JSON shape (the same shape
            // that Nabu's exported calibrations use, which is what
            // Calibration.PayloadJson stores verbatim).
            var toolCalibration = new ToolCalibration(
                id:                   Guid.NewGuid(),
                toolId:               tool.Id,
                name:                 request.CalibrationName,
                magnetometerCount:    nMags,
                calibrationDate:      nowUtc,
                calibratedBy:         request.CalibratedBy,
                accelerometerAxisPermutation:  ToJaggedInt(mardukResult.Gpm),
                accelerometerBias:             mardukResult.GOffset,
                accelerometerScaleFactor:      mardukResult.GSF,
                accelerometerAlignmentAngles:  mardukResult.GAlign,
                magnetometerAxisPermutation:   ToJaggedDouble(mardukResult.Bpm),
                magnetometerBias:              mardukResult.BOffset,
                magnetometerScaleFactor:       mardukResult.BSF,
                magnetometerAlignmentAngles:   mardukResult.BAlign,
                magnetometerLocations:         mardukResult.BLoc);

            var payloadJson = JsonSerializer.Serialize(toolCalibration, PayloadJsonOptions);
            var parsedShotsJson = JsonSerializer.Serialize(parsedShots, PayloadJsonOptions);
            var rawBinariesZip = ZipBinaries(binaries);

            // Auto-supersede prior current calibration for this tool.
            // Wizard already warned the user; we don't second-guess here.
            var prior = await TryGetCurrentCalibrationAsync(db, tool.Id, ct);
            if (prior is not null)
                prior.IsSuperseded = true;

            var calibration = new Calibration(
                toolId:          tool.Id,
                serialNumber:    tool.SerialNumber,
                calibrationDate: new DateTimeOffset(nowUtc, TimeSpan.Zero),
                payloadJson:     payloadJson)
            {
                CalibratedBy        = request.CalibratedBy,
                MagnetometerCount   = nMags,
                IsNominal           = IsNominal(mardukResult),
                IsSuperseded        = false,   // newest wins
                Source              = Core.Master.Tools.Enums.CalibrationSource.ComputedInEnki,
                Notes               = request.Notes,
                RawShotBinariesZip  = rawBinariesZip,
                ParsedShotsJson     = parsedShotsJson,
            };

            db.Calibrations.Add(calibration);
            await db.SaveChangesAsync(ct);

            lock (session.Lock)
            {
                session.SavedCalibrationId      = calibration.Id;
                session.SupersededCalibrationId = prior?.Id;
            }
            Store(session);

            return new ProcessingSaveResultDto(calibration.Id, prior?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Calibration save failed for session {SessionId}", sessionId);
            lock (session.Lock)
            {
                session.State = ProcessingSessionState.Computed;  // roll back so user can retry
                session.Error = $"Save failed: {ex.Message}";
            }
            Store(session);
            throw;
        }
    }

    // ========================= internals =========================

    private async Task RunParsePassAsync(Session session)
    {
        try
        {
            var tool = session.Tool;
            var fwVersion = ParseFirmware(tool.FirmwareVersion);
            var config = (AMR.Core.Enums.ToolConfiguration)tool.Configuration;
            var size = (AMR.Core.Enums.ToolSize)tool.Size;
            var toolInfo = new ToolInfo(
                tool.SerialNumber, fwVersion, config, size,
                tool.MagnetometerCount, tool.AccelerometerCount);
            var options = new WarriorParsingOptions
            {
                ToolInfo = toolInfo,
                FrameRegistry = _frameRegistry,
            };

            // Parallel parse + NarrowBand for all 25 shots (0..24). Index 0
            // is the loop-not-energized baseline (0.bin); 1..24 are the
            // active shots. Each thread gets its own NarrowBandShotProcessor
            // (the analyzer is stateful). Writes into session arrays under
            // the session lock; the cache sees the updated session on the
            // next GetStatus poll. Loop bound and array indexing are
            // shot-number-aligned (idx == shotIndex), so baseline = idx 0.
            var bag = new ConcurrentBag<int>();
            await Task.Run(() => Parallel.For(0, 25,
                () => new NarrowBandShotProcessor(new NarrowBandAnalyzer()),
                (idx, _, processor) =>
                {
                    int shotIndex = idx;
                    var bytes = session.RawBinaries[shotIndex];
                    var parsed = ParseAndProcessShot(bytes, shotIndex, options, tool.MagnetometerCount, processor);

                    var preview = BuildPreview(shotIndex, parsed);

                    lock (session.Lock)
                    {
                        session.ParsedShots[idx] = parsed;
                        session.Previews[idx]    = preview;
                    }
                    bag.Add(idx);
                    return processor;
                },
                _ => { }));

            lock (session.Lock)
            {
                session.State = ProcessingSessionState.ReadyForCompute;
            }
            Store(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parse pass failed for session {SessionId}", session.Id);
            lock (session.Lock)
            {
                session.State = ProcessingSessionState.Failed;
                session.Error = $"Parse failed: {ex.Message}";
            }
            Store(session);
        }
    }

    private CalibrationShotData ParseAndProcessShot(
        byte[] bytes, int shotIndex, WarriorParsingOptions options, int magnetometerCount,
        NarrowBandShotProcessor processor)
    {
        // Marduk's WarriorBinaryReader takes a file path. Tee bytes into a
        // per-shot temp file (cheap on SSD, 24 of these per session).
        var tempPath = Path.Combine(Path.GetTempPath(), $"enki-cal-{Guid.NewGuid():N}-{shotIndex}.bin");
        try
        {
            File.WriteAllBytes(tempPath, bytes);

            var frames = WarriorBinaryReader.ReadFrames(tempPath, options, _frameRegistry).ToList();
            var frame14 = frames.Last(f => f.FrameId == 14);
            var accel = ((IHasScaledAccelerometer)frame14).GetScaledAccelerometer();
            var hdr   = (IHasAcquisitionInfo)frame14;
            var gravity = new[] { accel.X, accel.Y, accel.Z };

            var activeFrameId = frames
                .Where(f => f.FrameId != 14)
                .GroupBy(f => f.FrameId)
                .OrderByDescending(g => g.Count())
                .First().Key;

            var activeFrames = frames.Where(f => f.FrameId == activeFrameId).ToList();
            var nSamples = activeFrames.Count;
            var nMagAxis = magnetometerCount * 3;
            var magData = new double[nSamples, nMagAxis];
            for (int s = 0; s < nSamples; s++)
            {
                var hasM = (IHasScaledMagnetometers)activeFrames[s];
                for (int m = 0; m < magnetometerCount; m++)
                {
                    var mag = hasM.GetScaledMagnetometer(m);
                    magData[s, m * 3 + 0] = mag.X;
                    magData[s, m * 3 + 1] = mag.Y;
                    magData[s, m * 3 + 2] = mag.Z;
                }
            }

            // sampleRate fixed at 100 Hz for the parse pass; the operator's
            // override (via ProcessingComputeRequestDto.SampleRateHz) only
            // applies at compute time. NarrowBand's frequency fit is robust
            // to a sample-rate mismatch within a factor of ~2.
            return processor.Process(
                shotIndex:           shotIndex,
                magnetometerSamples: magData,
                gravity:             gravity,
                sampleRateHz:        100.0,
                nMags:               magnetometerCount,
                manualSign:          1.0,
                temperature:         hdr.Temperature,
                timestamp:           hdr.Timestamp,
                shotTimeSeconds:     nSamples / 100.0,
                cutoffHz:            null,
                bandwidthHz:         0.0);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static ProcessingShotPreviewDto BuildPreview(int shotIndex, CalibrationShotData parsed)
    {
        // |G| from raw gravity (the parsed shot doesn't carry it back, so
        // recompute would need the gravity vector — keep it simple: report
        // 0 for now and let the operator gauge from F0 + acollinearity.
        // TODO Phase-4.1: surface raw |G| via a second array on Session.
        var nMags = parsed.Acollinearity?.Length ?? 0;
        return new ProcessingShotPreviewDto(
            ShotIndex:           shotIndex,
            SampleCount:         (int)Math.Round(parsed.ShotTimeSeconds * 100),
            FrequencyHz:         parsed.F0,
            TemperatureC:        parsed.Temperature,
            GravityMagnitude:    0,
            PerMagAcollinearity: parsed.Acollinearity?.ToList() ?? []);
    }

    private static IReadOnlyList<ProcessingShotDiagnosticDto> BuildDiagnostics(
        CalibrationComputationResult result,
        int[] enabledShotIndices)
    {
        var enabledSet = enabledShotIndices.ToHashSet();
        var nShots = result.CalibratedGravity?.Length ?? 24;
        var diagnostics = new ProcessingShotDiagnosticDto[nShots];

        for (int i = 0; i < nShots; i++)
        {
            var cg = result.CalibratedGravity?[i];
            var calG = cg is { Length: 3 } ? Math.Sqrt(cg[0]*cg[0] + cg[1]*cg[1] + cg[2]*cg[2]) : 0;

            var perMag = new List<double>();
            if (result.CalibratedMagnetics?[i] is { } magsForShot)
            {
                foreach (var cb in magsForShot)
                {
                    var calB = cb is { Length: 3 } ? Math.Sqrt(cb[0]*cb[0] + cb[1]*cb[1] + cb[2]*cb[2]) : 0;
                    perMag.Add(calB);
                }
            }

            diagnostics[i] = new ProcessingShotDiagnosticDto(
                ShotIndex:               i + 1,
                Enabled:                 enabledSet.Contains(i + 1),
                CalibratedGTotal:        calG,
                CalibratedBTotalPerMag:  perMag);
        }

        return diagnostics;
    }

    private static (double[,] gpm, double[][,] bpm, double[] bLoc) GetToolGeometry(int magnetometerCount, string generation) =>
        magnetometerCount switch
        {
            4 => (ToolGeometryPresets.StandardGpm, ToolGeometryPresets.G4Bpm, ToolGeometryPresets.G4BLoc),
            3 => (ToolGeometryPresets.StandardGpm, ToolGeometryPresets.G2Bpm, ToolGeometryPresets.G2BLoc),
            1 when generation == "G1" => (ToolGeometryPresets.BoxGpm, ToolGeometryPresets.BoxBpm, ToolGeometryPresets.BoxBLoc),
            _ => (ToolGeometryPresets.StandardGpm, ToolGeometryPresets.RotaryBpm, ToolGeometryPresets.RotaryBLoc),
        };

    private static FirmwareVersion ParseFirmware(string firmwareVersion)
    {
        var parts = firmwareVersion.Split('.', 2);
        var major = parts.Length > 0 && int.TryParse(parts[0], out var mj) ? mj : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0;
        return new FirmwareVersion(major, minor);
    }

    private static int[][] ToJaggedInt(double[,] m)
    {
        var rows = m.GetLength(0);
        var cols = m.GetLength(1);
        var jagged = new int[rows][];
        for (int r = 0; r < rows; r++)
        {
            jagged[r] = new int[cols];
            for (int c = 0; c < cols; c++)
                jagged[r][c] = (int)m[r, c];
        }
        return jagged;
    }

    private static double[][][] ToJaggedDouble(double[][,] perMag)
    {
        var nMags = perMag.Length;
        var jagged = new double[nMags][][];
        for (int k = 0; k < nMags; k++)
        {
            var rows = perMag[k].GetLength(0);
            var cols = perMag[k].GetLength(1);
            jagged[k] = new double[rows][];
            for (int r = 0; r < rows; r++)
            {
                jagged[k][r] = new double[cols];
                for (int c = 0; c < cols; c++)
                    jagged[k][r][c] = perMag[k][r, c];
            }
        }
        return jagged;
    }

    private static byte[] ZipBinaries(IReadOnlyDictionary<int, byte[]> binaries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 1; i <= 24; i++)
            {
                var entry = archive.CreateEntry($"{i}.bin", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(binaries[i], 0, binaries[i].Length);
            }
        }
        return ms.ToArray();
    }

    private static bool IsNominal(CalibrationComputationResult result) =>
        (result.GOffset?.All(v => v == 0.0) ?? true) &&
        (result.BOffset?.All(v => v == 0.0) ?? true);

    private static async Task<Calibration?> TryGetCurrentCalibrationAsync(
        Data.EnkiMasterDbContext db, Guid toolId, CancellationToken ct)
    {
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(
                db.Calibrations
                    .Where(c => c.ToolId == toolId && !c.IsSuperseded),
                ct);
    }

    private void Store(Session session) => _cache.Set(CacheKey(session.Id), session, SessionTtl);
    private Session? TryGet(Guid sessionId) => _cache.TryGetValue(CacheKey(sessionId), out Session? s) ? s : null;
    private static string CacheKey(Guid id) => $"calproc:{id:N}";

    // Internal session state. Mutated under Session.Lock by the parse Task
    // and by Compute / Save. Cached by the service; not exposed externally.
    private sealed class Session
    {
        public Guid Id { get; }
        public Tool Tool { get; }
        public IReadOnlyDictionary<int, byte[]> RawBinaries { get; }

        // Length 25 — index 0 is the 0.bin baseline (loop not energized),
        // 1..24 are active shots. Marduk only consumes 1..24.
        public CalibrationShotData?[] ParsedShots { get; } = new CalibrationShotData?[25];
        public ProcessingShotPreviewDto?[] Previews { get; } = new ProcessingShotPreviewDto?[25];

        public ProcessingSessionState State { get; set; } = ProcessingSessionState.Parsing;
        public string? Error { get; set; }

        public ProcessingComputeRequestDto? LastComputeRequest { get; set; }
        public CalibrationComputationResult? LastMardukResult { get; set; }
        public ProcessingResultDto? Result { get; set; }

        public Guid? SavedCalibrationId { get; set; }
        public Guid? SupersededCalibrationId { get; set; }

        public object Lock { get; } = new();

        public Session(Guid id, Tool tool, IReadOnlyDictionary<int, byte[]> binaries)
        {
            Id = id;
            Tool = tool;
            RawBinaries = binaries;
        }
    }
}

