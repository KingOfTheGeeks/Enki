using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;

namespace SDI.Enki.Infrastructure.Data;

/// <summary>
/// Loads the curated Tool + Calibration fleet into the master DB on first
/// boot. Source files live under <c>Data/Seed/{Tools,Calibrations}/*.json</c>
/// (copied to the bin folder by the csproj). Carried over verbatim from
/// Nabu — Nabu's <c>ToolMetadata</c> JSON shape and <c>CalibrationData</c>
/// JSON shape are both preserved here as the canonical fleet record.
///
/// Idempotent per-table: if the Tools table already has rows it skips Tools,
/// likewise Calibrations. Safe to run on every host startup. Unlike the
/// EF <c>HasData</c> seed (used for tiny static rows in MasterSeedData),
/// this is a runtime loader so the migration history isn't bloated by
/// large opaque calibration payloads.
/// </summary>
public static class MasterDataSeeder
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static async Task SeedAsync(
        EnkiMasterDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        var seedRoot = Path.Combine(AppContext.BaseDirectory, "Data", "Seed");

        await SeedToolsAsync(db, seedRoot, logger, ct);
        await SeedCalibrationsAsync(db, seedRoot, logger, ct);
    }

    private static async Task SeedToolsAsync(
        EnkiMasterDbContext db,
        string seedRoot,
        ILogger logger,
        CancellationToken ct)
    {
        if (await db.Tools.AnyAsync(ct))
            return;

        var folder = Path.Combine(seedRoot, "Tools");
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("Tool seed folder not found at {Folder}; skipping Tool seed.", folder);
            return;
        }

        var tools = new List<Tool>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var dto = JsonSerializer.Deserialize<ToolSeedJson>(json, ReadOptions);
                if (dto is null)
                {
                    logger.LogWarning("Tool seed file {File} deserialized to null; skipping.", file);
                    continue;
                }

                var firmware = $"{dto.FirmwareVersion.Major}.{dto.FirmwareVersion.Minor}";
                var tool = new Tool(dto.SerialNumber, firmware, dto.MagnetometerCount, dto.AccelerometerCount)
                {
                    Id = dto.Id,
                    Configuration = dto.Configuration,
                    Size = dto.Size,
                    Generation = Tool.InferGeneration(dto.FirmwareVersion.Major, dto.FirmwareVersion.Minor, dto.Configuration, dto.Size),
                    Status = ToolStatus.Active,
                };
                tools.Add(tool);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read seed tool from {File}; skipping.", file);
            }
        }

        if (tools.Count == 0)
        {
            logger.LogInformation("No tool seed files found in {Folder}.", folder);
            return;
        }

        db.Tools.AddRange(tools);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} tools from {Folder}.", tools.Count, folder);
    }

    private static async Task SeedCalibrationsAsync(
        EnkiMasterDbContext db,
        string seedRoot,
        ILogger logger,
        CancellationToken ct)
    {
        if (await db.Calibrations.AnyAsync(ct))
            return;

        var folder = Path.Combine(seedRoot, "Calibrations");
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("Calibration seed folder not found at {Folder}; skipping Calibration seed.", folder);
            return;
        }

        // Build toolId -> serialNumber once. Calibrations only carry toolId,
        // and Calibration.SerialNumber is denormalized for "what cals does
        // tool 1000093 have?" lookups without joining.
        var serialByToolId = await db.Tools
            .ToDictionaryAsync(t => t.Id, t => t.SerialNumber, ct);

        var calibrations = new List<Calibration>();
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var meta = JsonSerializer.Deserialize<CalibrationSeedMeta>(json, ReadOptions);
                if (meta is null)
                {
                    logger.LogWarning("Calibration seed file {File} deserialized to null; skipping.", file);
                    continue;
                }

                if (!serialByToolId.TryGetValue(meta.ToolId, out var serial))
                {
                    logger.LogWarning(
                        "Calibration {File} references unknown ToolId {ToolId}; skipping.",
                        file, meta.ToolId);
                    continue;
                }

                var calibrationDate = new DateTimeOffset(meta.CalibrationDate, TimeSpan.Zero);
                var cal = new Calibration(meta.ToolId, serial, calibrationDate, json)
                {
                    Id = meta.Id,
                    CalibratedBy = meta.CalibratedBy,
                    MagnetometerCount = meta.MagnetometerCount,
                    IsNominal = IsNominalPayload(meta),
                    Source = CalibrationSource.Migrated,
                };
                calibrations.Add(cal);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read seed calibration from {File}; skipping.", file);
            }
        }

        if (calibrations.Count == 0)
        {
            logger.LogInformation("No calibration seed files found in {Folder}.", folder);
            return;
        }

        // Mark all but the newest calibration per tool as superseded so the
        // "latest cal" UI defaults match Nabu's behaviour without needing
        // a max(date) subquery on every read.
        foreach (var byTool in calibrations.GroupBy(c => c.ToolId))
        {
            var latest = byTool.MaxBy(c => c.CalibrationDate);
            foreach (var cal in byTool)
                cal.IsSuperseded = !ReferenceEquals(cal, latest);
        }

        db.Calibrations.AddRange(calibrations);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} calibrations from {Folder}.", calibrations.Count, folder);
    }

    /// <summary>
    /// Mirrors Nabu's <c>CalibrationData.IsNominal</c>: a calibration is
    /// nominal/placeholder when every accelerometer + magnetometer bias is
    /// zero. Phase-1 calibrations are read-only, so this lets list views
    /// hide synthetic seed rows without scanning the full payload.
    /// </summary>
    private static bool IsNominalPayload(CalibrationSeedMeta meta) =>
        AllZero(meta.AccelerometerBias) && AllZero(meta.MagnetometerBias);

    private static bool AllZero(double[]? values) =>
        values is null || values.All(v => v == 0.0);

    // Mirror of Nabu's ToolMetadata JSON shape — kept private here so the
    // seed doesn't drag a Nabu reference into Infrastructure. Property names
    // are case-insensitive on read (ReadOptions), so PascalCase here matches
    // both the Nabu PascalCase tool files and the camelCase calibration files.
    private sealed record ToolSeedJson(
        Guid Id,
        int SerialNumber,
        FirmwareVersionJson FirmwareVersion,
        int Configuration,
        int Size,
        int MagnetometerCount,
        int AccelerometerCount);

    private sealed record FirmwareVersionJson(int Major, int Minor);

    // Partial deserialize of Nabu's CalibrationData — metadata fields the
    // Calibration entity stores as columns, plus the bias arrays so we can
    // compute IsNominal without a second pass. The full JSON round-trips
    // into Calibration.PayloadJson (Marduk's ToolCalibration shape; field-
    // for-field compatible with Nabu's calibration JSON).
    private sealed record CalibrationSeedMeta(
        Guid Id,
        Guid ToolId,
        DateTime CalibrationDate,
        string? CalibratedBy,
        int MagnetometerCount,
        double[]? AccelerometerBias,
        double[]? MagnetometerBias);
}
