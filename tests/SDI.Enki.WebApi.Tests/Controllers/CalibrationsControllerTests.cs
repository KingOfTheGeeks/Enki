using System.Text.Json;
using AMR.Core.Calibration.Export;
using AMR.Core.Calibration.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Settings;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Calibrations.Processing;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Coverage for <see cref="CalibrationsController"/> — processing-defaults
/// fallbacks, calibration detail with Tool join, and the .mpf download
/// gate (only current/non-nominal cals are downloadable).
/// </summary>
public class CalibrationsControllerTests
{
    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"calibrations-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private sealed class FakeMatExporter : ICalibrationMatExporter
    {
        public byte[]? LastExportedBytes { get; private set; }
        public ToolCalibration? LastInput { get; private set; }
        public byte[] Export(ToolCalibration calibration)
        {
            LastInput = calibration;
            LastExportedBytes = [0x4D, 0x41, 0x54];   // "MAT" — placeholder bytes
            return LastExportedBytes;
        }
        public void Export(ToolCalibration calibration, Stream output)
        {
            var bytes = Export(calibration);
            output.Write(bytes, 0, bytes.Length);
        }
    }

    private static CalibrationsController NewSut(
        EnkiMasterDbContext db, FakeMatExporter? exporter = null)
        => new(db, exporter ?? new FakeMatExporter())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    /// <summary>
    /// Build a Tool that the InMemory provider will accept — the real
    /// schema tracks Generation as a SmartEnum conversion which on
    /// InMemory just persists the int value.
    /// </summary>
    private static Tool NewTool(int serial = 1000001)
    {
        return new Tool(serialNumber: serial, firmwareVersion: "1.55", magnetometerCount: 3, accelerometerCount: 1)
        {
            Generation = ToolGeneration.G2,
            Status     = ToolStatus.Active,
        };
    }

    private static Calibration NewCalibration(Tool tool, string payloadJson, int magCount = 3)
    {
        return new Calibration(
            toolId:           tool.Id,
            serialNumber:     tool.SerialNumber,
            calibrationDate:  DateTimeOffset.UtcNow.AddDays(-30),
            payloadJson:      payloadJson)
        {
            CalibratedBy      = "M.King",
            MagnetometerCount = magCount,
            Source            = CalibrationSource.ComputedInEnki,
        };
    }

    /// <summary>
    /// JSON payload that <see cref="JsonSerializer.Deserialize{ToolCalibration}"/>
    /// will accept — exact set of properties the JsonConstructor takes.
    /// </summary>
    private static string MinimalToolCalibrationJson(Guid id, Guid toolId, string name = "G2-001-2025-11-23")
        => JsonSerializer.Serialize(new
        {
            id,
            toolId,
            name,
            magnetometerCount = 3,
            calibrationDate = DateTime.UtcNow,
            calibratedBy = "M.King",
            accelerometerAxisPermutation = new[] { new[] { 1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, 1 } },
            accelerometerBias = new[] { 0.0, 0.0, 0.0 },
            accelerometerScaleFactor = new[] { 1.0, 1.0, 1.0 },
            accelerometerAlignmentAngles = new[] { 0.0, 0.0, 0.0 },
            magnetometerAxisPermutation = new[]
            {
                new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } },
                new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } },
                new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } },
            },
            magnetometerBias = new[] { 0.0, 0.0, 0.0 },
            magnetometerScaleFactor = new[] { 1.0, 1.0, 1.0 },
            magnetometerAlignmentAngles = new[] { 0.0, 0.0, 0.0 },
            magnetometerLocations = new[] { 0.0, 1.0, 2.0 },
        });

    // ---------- processing-defaults ----------

    [Fact]
    public async Task GetProcessingDefaults_NoSettings_ReturnsHardcodedFallbacks()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var dto = await sut.GetProcessingDefaults(CancellationToken.None);

        // Fallbacks declared inline in the controller — pinning them
        // here so a future change to a default is forced through code
        // review on this file too.
        Assert.Equal(1000.01,  dto.GTotal);
        Assert.Equal(46895.0,  dto.BTotal);
        Assert.Equal(59.867,   dto.DipDegrees);
        Assert.Equal(12.313,   dto.DeclinationDegrees);
        Assert.Equal(360.0,    dto.CoilConstant);
        Assert.Equal(89.44,    dto.ActiveBDipDegrees);
        Assert.Equal(100.0,    dto.SampleRateHz);
        Assert.Equal(1.0,      dto.ManualSign);
        Assert.Equal(6.01,     dto.DefaultCurrent);
        Assert.Equal("static", dto.MagSource);
        Assert.True(dto.IncludeDeclination);
    }

    [Fact]
    public async Task GetProcessingDefaults_PersistedSettings_OverrideFallbacks()
    {
        await using var db = NewDb();
        db.SystemSettings.AddRange(
            new SystemSetting(SystemSettingKeys.CalibrationDefaultGTotal,             "1234.56"),
            new SystemSetting(SystemSettingKeys.CalibrationDefaultBTotal,             "50000"),
            new SystemSetting(SystemSettingKeys.CalibrationDefaultMagSource,          "active"),
            new SystemSetting(SystemSettingKeys.CalibrationDefaultIncludeDeclination, "false"));
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var dto = await sut.GetProcessingDefaults(CancellationToken.None);

        Assert.Equal(1234.56, dto.GTotal);
        Assert.Equal(50000.0, dto.BTotal);
        Assert.Equal("active", dto.MagSource);
        Assert.False(dto.IncludeDeclination);
        // Untouched key still falls through to the hardcoded default.
        Assert.Equal(360.0, dto.CoilConstant);
    }

    [Fact]
    public async Task GetProcessingDefaults_MalformedDoubleSetting_FallsBackToDefault()
    {
        await using var db = NewDb();
        db.SystemSettings.Add(
            new SystemSetting(SystemSettingKeys.CalibrationDefaultGTotal, "not-a-number"));
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var dto = await sut.GetProcessingDefaults(CancellationToken.None);

        // ParseDouble fall-through: malformed value → hardcoded default.
        Assert.Equal(1000.01, dto.GTotal);
    }

    // ---------- detail ----------

    [Fact]
    public async Task Get_KnownId_ReturnsDetailWithToolDisplayName()
    {
        await using var db = NewDb();
        var tool = NewTool(serial: 1000099);
        db.Tools.Add(tool);
        await db.SaveChangesAsync();

        var cal = NewCalibration(tool, MinimalToolCalibrationJson(Guid.NewGuid(), tool.Id));
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(cal.Id, CancellationToken.None));
        var dto = Assert.IsType<CalibrationDetailDto>(ok.Value);
        Assert.Equal(cal.Id, dto.Id);
        Assert.Equal(1000099, dto.SerialNumber);
        // ToolDisplay.Name uses generation + short-serial form (G2-099).
        Assert.StartsWith("G2-", dto.ToolDisplayName);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.Get(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---------- file download ----------

    [Fact]
    public async Task DownloadFile_CurrentCalibration_ReturnsFileResultWithSafeFilename()
    {
        await using var db = NewDb();
        var tool = NewTool();
        db.Tools.Add(tool);
        await db.SaveChangesAsync();

        var cal = NewCalibration(tool,
            MinimalToolCalibrationJson(Guid.NewGuid(), tool.Id, name: "G2/001 v1"));
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();

        var exporter = new FakeMatExporter();
        var sut = NewSut(db, exporter);

        var result = await sut.DownloadFile(cal.Id, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/octet-stream", fileResult.ContentType);
        Assert.NotNull(exporter.LastExportedBytes);
        Assert.Equal(exporter.LastExportedBytes, fileResult.FileContents);
        // Filename sanitisation: '/' becomes '_'; .mpf extension preserved.
        Assert.DoesNotContain('/', fileResult.FileDownloadName);
        Assert.EndsWith(".mpf", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task DownloadFile_SupersededCalibration_ReturnsNotFound()
    {
        await using var db = NewDb();
        var tool = NewTool();
        db.Tools.Add(tool);
        await db.SaveChangesAsync();

        var cal = NewCalibration(tool, MinimalToolCalibrationJson(Guid.NewGuid(), tool.Id));
        cal.IsSuperseded = true;
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.DownloadFile(cal.Id, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task DownloadFile_NominalCalibration_ReturnsNotFound()
    {
        await using var db = NewDb();
        var tool = NewTool();
        db.Tools.Add(tool);
        await db.SaveChangesAsync();

        var cal = NewCalibration(tool, MinimalToolCalibrationJson(Guid.NewGuid(), tool.Id));
        cal.IsNominal = true;
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.DownloadFile(cal.Id, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task DownloadFile_UnknownId_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.DownloadFile(Guid.NewGuid(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }
}
