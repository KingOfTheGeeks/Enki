using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.CalibrationProcessing;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations.Processing;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Coverage for the validation + lookup paths on the calibration-
/// processing controller. The deep parse/compute pipeline is exercised
/// in <c>CalibrationProcessingService</c> tests; here we cover the
/// front-door validation + 404 + 409 surface so a route-wiring
/// regression is caught even without dragging the full pipeline in.
/// </summary>
public class CalibrationProcessingControllerTests
{
    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"calproc-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static CalibrationProcessingService NewService() =>
        new(new MemoryCache(new MemoryCacheOptions()), NullLogger<CalibrationProcessingService>.Instance);

    private static CalibrationProcessingController NewSut(
        EnkiMasterDbContext db,
        CalibrationProcessingService? service = null,
        IFormFileCollection? files = null)
    {
        var http = new DefaultHttpContext();
        if (files is not null)
        {
            http.Request.ContentType = "multipart/form-data; boundary=test";
            http.Request.Form = new FormCollection(
                fields: new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
                files:  files);
        }

        return new CalibrationProcessingController(db, service ?? NewService())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static IFormFile MakeFormFile(string fileName)
    {
        var bytes = new byte[] { 0x00 };
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, name: "files", fileName: fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream",
        };
    }

    /// <summary>
    /// Wraps a list of <see cref="IFormFile"/> as
    /// <see cref="IFormFileCollection"/> for the request stub. ASP.NET's
    /// own <see cref="FormFileCollection"/> already implements the
    /// interface; this helper exists so the test reads as
    /// <c>MakeFiles("0.bin", "1.bin", …)</c>.
    /// </summary>
    private static IFormFileCollection MakeFiles(IEnumerable<string> names)
    {
        var coll = new FormFileCollection();
        foreach (var n in names) coll.Add(MakeFormFile(n));
        return coll;
    }

    private static Tool NewTool(int serial = 1000001, ToolStatus? status = null) =>
        new(serialNumber: serial, firmwareVersion: "1.55", magnetometerCount: 3, accelerometerCount: 1)
        {
            Generation = ToolGeneration.G2,
            Status     = status ?? ToolStatus.Active,
        };

    private static ProcessingComputeRequestDto SampleComputeRequest(int currentsCount = 24) =>
        new(
            EnabledShotIndices: Enumerable.Range(1, 24).ToList(),
            GTotal:             1000.01,
            BTotal:             46895.0,
            DipDegrees:         59.867,
            DeclinationDegrees: 12.313,
            CoilConstant:       360.0,
            ActiveBDipDegrees:  89.44,
            SampleRateHz:       100.0,
            ManualSign:         1.0,
            CurrentsByShot:     Enumerable.Repeat(6.01, currentsCount).ToList());

    // ---------- Start ----------

    [Fact]
    public async Task Start_UnknownTool_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.Start(serial: 999999, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task Start_RetiredTool_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        db.Tools.Add(NewTool(serial: 1000010, status: ToolStatus.Retired));
        await db.SaveChangesAsync();

        var sut = NewSut(db);

        var result = await sut.Start(serial: 1000010, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
    }

    [Fact]
    public async Task Start_WrongFileCount_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        db.Tools.Add(NewTool(serial: 1000011));
        await db.SaveChangesAsync();

        // 24 files instead of 25 — the 0.bin baseline is missing.
        var sut = NewSut(db, files: MakeFiles(Enumerable.Range(1, 24).Select(i => $"{i}.bin")));

        var result = await sut.Start(serial: 1000011, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Start_BadFilenamePattern_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        db.Tools.Add(NewTool(serial: 1000012));
        await db.SaveChangesAsync();

        // 24 valid + 1 bogus name → exactly 25 files but one doesn't match the regex.
        var names = Enumerable.Range(1, 24).Select(i => $"{i}.bin").Concat(["garbage.bin"]);
        var sut = NewSut(db, files: MakeFiles(names));

        var result = await sut.Start(serial: 1000012, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    [Fact]
    public async Task Start_DuplicateShotIndex_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        db.Tools.Add(NewTool(serial: 1000013));
        await db.SaveChangesAsync();

        // 0.bin baseline + 1.bin..23.bin + a duplicated 5.bin = 25 files.
        var names = new List<string> { "0.bin" };
        names.AddRange(Enumerable.Range(1, 23).Select(i => $"{i}.bin"));
        names.Add("5.bin");

        var sut = NewSut(db, files: MakeFiles(names));

        var result = await sut.Start(serial: 1000013, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    // ---------- Status ----------

    [Fact]
    public void Status_UnknownSession_ReturnsNotFoundProblem()
    {
        using var db = NewDb();
        var sut = NewSut(db);

        var result = sut.Status(serial: 1, sessionId: Guid.NewGuid());

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---------- Compute ----------

    [Fact]
    public void Compute_UnknownSession_ReturnsNotFoundProblem()
    {
        using var db = NewDb();
        var sut = NewSut(db);

        var result = sut.Compute(
            serial:    1,
            sessionId: Guid.NewGuid(),
            request:   SampleComputeRequest());

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---------- Save ----------

    [Fact]
    public async Task Save_UnknownSession_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var result = await sut.Save(
            serial:    1,
            sessionId: Guid.NewGuid(),
            request:   new ProcessingSaveRequestDto(
                CalibrationName: "G2-001-2025-11-23",
                CalibratedBy:    "M.King",
                Notes:           null),
            ct:        CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }
}
