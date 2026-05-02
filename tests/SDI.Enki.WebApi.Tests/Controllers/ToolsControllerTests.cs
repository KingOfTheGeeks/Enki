using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tools;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Tools;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="ToolsController"/> Retire +
/// Reactivate handlers. Same shape as <see cref="TenantsControllerTests"/>:
/// in-memory DbContext, fresh DB per test, no full HTTP pipeline. Covers
/// the structured retirement metadata flow (the new RetireToolDto fields,
/// disposition→status mapping, replacement-tool resolution, idempotency,
/// reactivation field-clearing).
/// </summary>
public class ToolsControllerTests
{
    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"tools-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static ToolsController NewController(EnkiMasterDbContext db, string? userName = "testuser")
    {
        var controller = new ToolsController(db, new FakeCurrentUser(userName));
        var identity = new ClaimsIdentity(
            [new Claim("role", "enki-admin")],
            authenticationType: "Test",
            nameType: "name",
            roleType: "role");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static Tool SeedTool(
        EnkiMasterDbContext db,
        int serial = 1000093,
        ToolStatus? status = null)
    {
        var tool = new Tool(serial, "1.55", magnetometerCount: 3, accelerometerCount: 1)
        {
            Configuration = 2,
            Size          = 54000,
            Generation    = ToolGeneration.G2,
            Status        = status ?? ToolStatus.Active,
            RowVersion    = TestRowVersionBytes,
        };
        db.Tools.Add(tool);
        db.SaveChanges();
        return tool;
    }

    private static RetireToolDto Dto(
        string disposition = "Retired",
        DateOnly? effectiveDate = null,
        string reason = "Reason text.",
        int? replacementSerial = null,
        string? finalLocation = null,
        string? rowVersion = null) =>
        new(disposition, effectiveDate ?? new DateOnly(2025, 5, 1), reason,
            replacementSerial, finalLocation, rowVersion ?? TestRowVersion);

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedTypeSuffix)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.EndsWith(expectedTypeSuffix, problem.Type);
    }

    // ============================================================
    // Retire — happy paths
    // ============================================================

    [Theory]
    [InlineData("Retired", "Retired")]
    [InlineData("Scrapped", "Retired")]
    [InlineData("Sold", "Retired")]
    [InlineData("Transferred", "Retired")]
    [InlineData("ReturnedToOwner", "Retired")]
    [InlineData("Lost", "Lost")]
    public async Task Retire_MapsDispositionToStatus(string disposition, string expectedStatus)
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var result = await sut.Retire(tool.SerialNumber, Dto(disposition: disposition), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal(expectedStatus, reloaded.Status.Name);
        Assert.Equal(disposition, reloaded.Disposition?.Name);
    }

    [Fact]
    public async Task Retire_PopulatesAllRetirementColumns()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db, userName: "k.alvarez");

        var dto = Dto(
            disposition: "Sold",
            effectiveDate: new DateOnly(2025, 6, 30),
            reason: "Sold to Vector Drilling Services.",
            finalLocation: "Calgary");
        var result = await sut.Retire(tool.SerialNumber, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal("Sold", r.Disposition?.Name);
        Assert.Equal(new DateTimeOffset(2025, 6, 30, 0, 0, 0, TimeSpan.Zero), r.RetiredAt);
        Assert.Equal("k.alvarez", r.RetiredBy);
        Assert.Equal("Sold to Vector Drilling Services.", r.RetirementReason);
        Assert.Equal("Calgary", r.RetirementLocation);
        Assert.Null(r.ReplacementToolId);
    }

    [Fact]
    public async Task Retire_WithReplacementSerial_ResolvesToReplacementToolId()
    {
        await using var db = NewDb();
        var retiring = SeedTool(db, serial: 1000093);
        var replacement = SeedTool(db, serial: 1000099);
        var sut = NewController(db);

        var result = await sut.Retire(retiring.SerialNumber,
            Dto(disposition: "Transferred", replacementSerial: 1000099),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == retiring.Id);
        Assert.Equal(replacement.Id, r.ReplacementToolId);
    }

    // ============================================================
    // Retire — error paths
    // ============================================================

    [Fact]
    public async Task Retire_WithUnknownDisposition_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var result = await sut.Retire(tool.SerialNumber,
            Dto(disposition: "PutOnTheShelfMaybe"),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Retire_WithUnknownReplacementSerial_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var result = await sut.Retire(tool.SerialNumber,
            Dto(replacementSerial: 99_999_999),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        // Tool should be untouched.
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal(ToolStatus.Active, r.Status);
        Assert.Null(r.Disposition);
    }

    [Fact]
    public async Task Retire_WithSelfReplacement_ReturnsValidationProblem()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var result = await sut.Retire(tool.SerialNumber,
            Dto(replacementSerial: tool.SerialNumber),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Retire_UnknownTool_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Retire(404404, Dto(), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Retire — idempotency
    // ============================================================

    [Fact]
    public async Task Retire_SameFieldsTwice_SecondCallIsIdempotent()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var dto = Dto(disposition: "Sold", reason: "First call.", finalLocation: "Yard");
        var first = await sut.Retire(tool.SerialNumber, dto, CancellationToken.None);
        Assert.IsType<NoContentResult>(first);

        // Reload to grab the new RowVersion (ApplyClientRowVersion needs it
        // to match what's in the DB now). InMemory doesn't bump RowVersion
        // automatically, so reuse the seed bytes.
        var second = await sut.Retire(tool.SerialNumber, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(second);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal("Sold", r.Disposition?.Name);
        Assert.Equal("First call.", r.RetirementReason);
    }

    [Fact]
    public async Task Retire_DifferentDispositionAfterFirstRetire_UpdatesFields()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var first = await sut.Retire(tool.SerialNumber,
            Dto(disposition: "Retired", reason: "End-of-life."),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(first);

        var amend = await sut.Retire(tool.SerialNumber,
            Dto(disposition: "Sold", reason: "Actually sold to vendor."),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(amend);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal("Sold", r.Disposition?.Name);
        Assert.Equal("Actually sold to vendor.", r.RetirementReason);
    }

    // ============================================================
    // Reactivate — clears retirement columns
    // ============================================================

    [Fact]
    public async Task Reactivate_ClearsAllRetirementColumns()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        await sut.Retire(tool.SerialNumber,
            Dto(disposition: "Sold",
                replacementSerial: null,
                finalLocation: "Yard"),
            CancellationToken.None);

        var result = await sut.Reactivate(tool.SerialNumber,
            new LifecycleTransitionDto(RowVersion: TestRowVersion),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal(ToolStatus.Active, r.Status);
        Assert.Null(r.Disposition);
        Assert.Null(r.RetiredAt);
        Assert.Null(r.RetiredBy);
        Assert.Null(r.RetirementReason);
        Assert.Null(r.RetirementLocation);
        Assert.Null(r.ReplacementToolId);
    }

    [Fact]
    public async Task Reactivate_FromLost_FlipsToActiveAndClears()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        await sut.Retire(tool.SerialNumber,
            Dto(disposition: "Lost", reason: "Lost in well."),
            CancellationToken.None);
        // Confirm it landed in Lost first.
        var afterLose = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal(ToolStatus.Lost, afterLose.Status);

        var result = await sut.Reactivate(tool.SerialNumber,
            new LifecycleTransitionDto(RowVersion: TestRowVersion),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal(ToolStatus.Active, r.Status);
        Assert.Null(r.Disposition);
    }

    [Fact]
    public async Task Reactivate_AlreadyActive_IsIdempotent()
    {
        await using var db = NewDb();
        var tool = SeedTool(db);
        var sut = NewController(db);

        var result = await sut.Reactivate(tool.SerialNumber,
            new LifecycleTransitionDto(RowVersion: TestRowVersion),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var r = await db.Tools.AsNoTracking().FirstAsync(t => t.Id == tool.Id);
        Assert.Equal(ToolStatus.Active, r.Status);
    }

    // ---- fakes ----

    private sealed class FakeCurrentUser(string? userName) : ICurrentUser
    {
        public string? UserId => userName;
        public string? UserName => userName;
    }
}
