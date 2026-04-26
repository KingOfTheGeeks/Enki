using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Wells;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="WellsController"/>. Uses a
/// per-test InMemory tenant context via
/// <see cref="FakeTenantDbContextFactory"/>; each test seeds its own
/// Job because Wells now require a Job FK (NOT NULL).
/// </summary>
public class WellsControllerTests
{
    private static (WellsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new WellsController(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
                RouteData   = new RouteData
                {
                    Values = { ["tenantCode"] = "PERMIAN" },
                },
            },
        };
        return (controller, factory);
    }

    private static async Task<Guid> SeedJobAsync(FakeTenantDbContextFactory factory)
    {
        await using var db = factory.NewActiveContext();
        var job = new Job("Crest-22-14H", "Test job", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<int> SeedWellAsync(
        FakeTenantDbContextFactory factory,
        Guid jobId,
        string name = "Lone Star 14H",
        WellType? type = null)
    {
        await using var db = factory.NewActiveContext();
        var well = new Well(jobId, name, type ?? WellType.Target);
        db.Wells.Add(well);
        await db.SaveChangesAsync();
        return well.Id;
    }

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedTypeSuffix)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.EndsWith(expectedTypeSuffix, problem.Type);
    }

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_UnknownJob_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        AssertProblem(await sut.List(Guid.NewGuid(), CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task List_NoWells_ReturnsEmpty()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<WellSummaryDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task List_WithWells_ReturnsSummariesOrderedByName()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        await SeedWellAsync(factory, jobId, "Zebra-1H");
        await SeedWellAsync(factory, jobId, "Alpha-1H");
        await SeedWellAsync(factory, jobId, "Mike-1H");

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, CancellationToken.None));
        var rows = ((IEnumerable<WellSummaryDto>)ok.Value!).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Alpha-1H", "Mike-1H", "Zebra-1H" }, rows.Select(w => w.Name));
        Assert.All(rows, r => Assert.Equal(0, r.SurveyCount));
    }

    [Fact]
    public async Task List_ScopesToTheGivenJobOnly()
    {
        var (sut, factory) = NewSut();
        var jobA = await SeedJobAsync(factory);
        var jobB = await SeedJobAsync(factory);
        await SeedWellAsync(factory, jobA, "A-Well");
        await SeedWellAsync(factory, jobB, "B-Well");

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobA, CancellationToken.None));
        var rows = ((IEnumerable<WellSummaryDto>)ok.Value!).ToList();
        Assert.Single(rows);
        Assert.Equal("A-Well", rows[0].Name);
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId, "Lone Star 14H", WellType.Target);

        var result = await sut.Get(jobId, wellId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<WellDetailDto>(ok.Value);
        Assert.Equal(wellId, dto.Id);
        Assert.Equal("Lone Star 14H", dto.Name);
        Assert.Equal("Target", dto.Type);
    }

    [Fact]
    public async Task Get_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.Get(jobId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Get_WellUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobA = await SeedJobAsync(factory);
        var jobB = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);

        AssertProblem(await sut.Get(jobB, wellId, CancellationToken.None), 404, "/not-found");
    }

    // ============================================================
    // Create
    // ============================================================

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Create(jobId,
            new CreateWellDto(Name: "New Well", Type: "Target"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<WellSummaryDto>(created.Value);
        Assert.Equal("New Well", summary.Name);

        await using var db = factory.NewActiveContext();
        var stored = await db.Wells.AsNoTracking().FirstAsync();
        Assert.Equal(jobId, stored.JobId);
        Assert.Equal("system", stored.CreatedBy);
    }

    [Fact]
    public async Task Create_UnknownJob_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        AssertProblem(await sut.Create(Guid.NewGuid(),
            new CreateWellDto("X", "Target"),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Create(jobId,
            new CreateWellDto(Name: "Bad", Type: "Bogus"),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_ValidDto_RenamesAndStampsUpdatedAudit()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId, "Old", WellType.Target);

        var result = await sut.Update(jobId, wellId,
            new UpdateWellDto(Name: "New", Type: "Offset"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Wells.AsNoTracking().FirstAsync(w => w.Id == wellId);
        Assert.Equal("New", reloaded.Name);
        Assert.Equal(WellType.Offset, reloaded.Type);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Update_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.Update(jobId, 99999,
            new UpdateWellDto("x", "Target"),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_WellUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobA   = await SeedJobAsync(factory);
        var jobB   = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);

        AssertProblem(await sut.Update(jobB, wellId,
            new UpdateWellDto("x", "Target"),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId,
            new UpdateWellDto("x", "NotAWellType"),
            CancellationToken.None), 400, "/validation");
    }

    // ============================================================
    // Delete
    // ============================================================

    [Fact]
    public async Task Delete_NoChildren_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Wells.AnyAsync(w => w.Id == wellId));
    }

    [Fact]
    public async Task Delete_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.Delete(jobId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_WithChildSurvey_ReturnsConflictProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Surveys.Add(new Survey(wellId, depth: 1000, inclination: 0, azimuth: 0));
            await db.SaveChangesAsync();
        }

        var result = await sut.Delete(jobId, wellId, CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
        await using var db2 = factory.NewActiveContext();
        Assert.True(await db2.Wells.AnyAsync(w => w.Id == wellId));
    }

    [Fact]
    public async Task Delete_WithChildTieOn_ReturnsConflictProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0));
            await db.SaveChangesAsync();
        }

        AssertProblem(await sut.Delete(jobId, wellId, CancellationToken.None), 409, "/conflict");
    }
}
