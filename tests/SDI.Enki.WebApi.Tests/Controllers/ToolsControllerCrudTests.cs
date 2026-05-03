using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Tools;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// CRUD coverage for <see cref="ToolsController"/> — list filters, detail,
/// create, update, calibration listing. The retire / reactivate paths
/// are exercised in <c>ToolsControllerTests</c>; this file fills in the
/// rest of the surface so coverage isn't dominated by the lifecycle
/// transitions.
/// </summary>
public class ToolsControllerCrudTests
{
    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"tools-crud-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private sealed class FakeCurrentUser(string? userName) : ICurrentUser
    {
        public string? UserId => userName;
        public string? UserName => userName;
    }

    private static ToolsController NewController(EnkiMasterDbContext db)
    {
        var controller = new ToolsController(db, new FakeCurrentUser("testuser"));
        var identity = new ClaimsIdentity(
            [new Claim("role", "enki-admin")], "Test", "name", "role");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static Tool SeedTool(
        EnkiMasterDbContext db,
        int serial = 1000099,
        string firmware = "1.55",
        ToolGeneration? generation = null,
        ToolStatus? status = null)
    {
        var tool = new Tool(serial, firmware, magnetometerCount: 3, accelerometerCount: 1)
        {
            Configuration = 2,
            Size          = 54000,
            Generation    = generation ?? ToolGeneration.G2,
            Status        = status ?? ToolStatus.Active,
            RowVersion    = TestRowVersionBytes,
        };
        db.Tools.Add(tool);
        db.SaveChanges();
        return tool;
    }

    // ---------- List ----------

    [Fact]
    public async Task List_ReturnsAllTools_OrderedBySerial()
    {
        await using var db = NewDb();
        SeedTool(db, serial: 1000003);
        SeedTool(db, serial: 1000001);
        SeedTool(db, serial: 1000002);

        var sut = NewController(db);
        var rows = (await sut.List(status: null, generation: null, ct: CancellationToken.None)).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1000001, 1000002, 1000003 }, rows.Select(r => r.SerialNumber));
    }

    [Fact]
    public async Task List_StatusFilter_LimitsToMatchingTools()
    {
        await using var db = NewDb();
        SeedTool(db, serial: 1000001, status: ToolStatus.Active);
        SeedTool(db, serial: 1000002, status: ToolStatus.Retired);
        SeedTool(db, serial: 1000003, status: ToolStatus.Lost);

        var sut = NewController(db);
        var active = (await sut.List(status: "Active", generation: null, ct: CancellationToken.None)).ToList();

        Assert.Single(active);
        Assert.Equal(1000001, active[0].SerialNumber);
    }

    [Fact]
    public async Task List_GenerationFilter_LimitsToMatchingGeneration()
    {
        await using var db = NewDb();
        SeedTool(db, serial: 1000001, generation: ToolGeneration.G2);
        SeedTool(db, serial: 1000002, generation: ToolGeneration.G4, firmware: "1.90");

        var sut = NewController(db);
        var g4 = (await sut.List(status: null, generation: "G4", ct: CancellationToken.None)).ToList();

        Assert.Single(g4);
        Assert.Equal(1000002, g4[0].SerialNumber);
    }

    [Fact]
    public async Task List_BothFilters_AndedTogether()
    {
        await using var db = NewDb();
        SeedTool(db, serial: 1000001, generation: ToolGeneration.G2, status: ToolStatus.Active);
        SeedTool(db, serial: 1000002, generation: ToolGeneration.G2, status: ToolStatus.Retired);
        SeedTool(db, serial: 1000003, generation: ToolGeneration.G4, firmware: "1.90", status: ToolStatus.Active);

        var sut = NewController(db);
        var rows = (await sut.List(status: "Active", generation: "G2", ct: CancellationToken.None)).ToList();

        Assert.Single(rows);
        Assert.Equal(1000001, rows[0].SerialNumber);
    }

    // ---------- Get ----------

    [Fact]
    public async Task Get_KnownSerial_ReturnsDetailDto()
    {
        await using var db = NewDb();
        var tool = SeedTool(db, serial: 1000077);

        var sut = NewController(db);
        var ok = Assert.IsType<OkObjectResult>(await sut.Get(1000077, CancellationToken.None));
        var dto = Assert.IsType<ToolDetailDto>(ok.Value);
        Assert.Equal(tool.Id, dto.Id);
        Assert.Equal(1000077, dto.SerialNumber);
        Assert.StartsWith("G2-", dto.DisplayName);
    }

    [Fact]
    public async Task Get_UnknownSerial_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Get(999999, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturns201()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var dto = new CreateToolDto(
            SerialNumber:       1000200,
            FirmwareVersion:    "1.55",
            Configuration:      2,
            Size:               54000,
            MagnetometerCount:  3,
            AccelerometerCount: 1,
            Generation:         "G2",
            Notes:              "Test tool");

        var result = await sut.Create(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<ToolDetailDto>(created.Value);
        Assert.Equal(1000200, detail.SerialNumber);
        Assert.Equal("G2",    detail.Generation);
        Assert.Equal("Active", detail.Status);

        // Persisted
        var reloaded = await db.Tools.AsNoTracking().FirstAsync(t => t.SerialNumber == 1000200);
        Assert.Equal("Test tool", reloaded.Notes);
    }

    [Fact]
    public async Task Create_OmitsGeneration_InfersFromFirmwareConfigSize()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        // 1.55 + config 2 + size 54000 → InferGeneration returns G2.
        var dto = new CreateToolDto(
            SerialNumber:    1000201,
            FirmwareVersion: "1.55",
            Configuration:   2,
            Size:            54000,
            Generation:      null);

        var result = await sut.Create(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<ToolDetailDto>(created.Value);
        Assert.Equal("G2", detail.Generation);
    }

    [Fact]
    public async Task Create_DuplicateSerial_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        SeedTool(db, serial: 1000202);

        var sut = NewController(db);
        var dto = new CreateToolDto(SerialNumber: 1000202, FirmwareVersion: "1.55");

        var result = await sut.Create(dto, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownGenerationName_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var dto = new CreateToolDto(
            SerialNumber:    1000203,
            FirmwareVersion: "1.55",
            Generation:      "Bogus");

        var result = await sut.Create(dto, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_ValidDto_PersistsChanges()
    {
        await using var db = NewDb();
        var tool = SeedTool(db, serial: 1000300);
        var sut = NewController(db);

        var dto = new UpdateToolDto(
            SerialNumber:       1000300,
            FirmwareVersion:    "1.90",
            Generation:         "G4",
            Configuration:      2,
            Size:               54000,
            MagnetometerCount:  4,
            AccelerometerCount: 1,
            Notes:              "Refreshed firmware",
            RowVersion:         TestRowVersion);

        var result = await sut.Update(1000300, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal("1.90",         reloaded.FirmwareVersion);
        Assert.Equal(ToolGeneration.G4, reloaded.Generation);
        Assert.Equal(4,              reloaded.MagnetometerCount);
    }

    [Fact]
    public async Task Update_RenameToCollidingSerial_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        SeedTool(db, serial: 1000401);
        var tool = SeedTool(db, serial: 1000402);
        var sut = NewController(db);

        var dto = new UpdateToolDto(
            SerialNumber:       1000401,                // collides with the other tool
            FirmwareVersion:    tool.FirmwareVersion,
            Generation:         "G2",
            Configuration:      tool.Configuration,
            Size:               tool.Size,
            MagnetometerCount:  tool.MagnetometerCount,
            AccelerometerCount: tool.AccelerometerCount,
            RowVersion:         TestRowVersion);

        var result = await sut.Update(1000402, dto, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownSerial_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var dto = new UpdateToolDto(
            SerialNumber:    999999, FirmwareVersion: "1.55", Generation: "G2",
            Configuration:   2, Size: 54000,
            MagnetometerCount: 3, AccelerometerCount: 1,
            RowVersion: TestRowVersion);

        var result = await sut.Update(999999, dto, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownGenerationName_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var tool = SeedTool(db, serial: 1000500);
        var sut = NewController(db);

        var dto = new UpdateToolDto(
            SerialNumber:    1000500, FirmwareVersion: "1.55",
            Generation:      "Bogus",   // not a SmartEnum value
            Configuration:   2, Size: 54000,
            MagnetometerCount: 3, AccelerometerCount: 1,
            RowVersion: TestRowVersion);

        var result = await sut.Update(1000500, dto, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, obj.StatusCode);
    }

    // ---------- ListCalibrations ----------

    [Fact]
    public async Task ListCalibrations_KnownTool_ReturnsCalibrationsOrderedByDateDesc()
    {
        await using var db = NewDb();
        var tool = SeedTool(db, serial: 1000600);
        var older = new Calibration(tool.Id, tool.SerialNumber,
            DateTimeOffset.UtcNow.AddDays(-365), "{}")
        {
            CalibratedBy = "older", MagnetometerCount = 3,
            Source = CalibrationSource.Imported, IsSuperseded = true,
        };
        var newer = new Calibration(tool.Id, tool.SerialNumber,
            DateTimeOffset.UtcNow.AddDays(-30), "{}")
        {
            CalibratedBy = "newer", MagnetometerCount = 3,
            Source = CalibrationSource.ComputedInEnki,
        };
        db.Calibrations.AddRange(older, newer);
        await db.SaveChangesAsync();

        var sut = NewController(db);
        var ok = Assert.IsType<OkObjectResult>(await sut.ListCalibrations(1000600, CancellationToken.None));
        var rows = ((IEnumerable<CalibrationSummaryDto>)ok.Value!).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("newer", rows[0].CalibratedBy);
        Assert.Equal("older", rows[1].CalibratedBy);
    }

    [Fact]
    public async Task ListCalibrations_UnknownTool_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.ListCalibrations(999999, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, obj.StatusCode);
    }
}
