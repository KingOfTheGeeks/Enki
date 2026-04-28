using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Wells.TieOns;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class TieOnsControllerTests
{
    private static (TieOnsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        // The auto-calc fires after every TieOn mutation; tests don't
        // assert on its behavior here (that's what SurveysControllerTests
        // covers), so the wrapped fake calculator is just satisfying the
        // controller's required dependency.
        var factory  = new FakeTenantDbContextFactory();
        var autoCalc = new MardukSurveyAutoCalculator(new FakeSurveyCalculator());
        var controller = new TieOnsController(factory, autoCalc)
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
        var job = new Job("Test", "Test job", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<int> SeedWellAsync(FakeTenantDbContextFactory factory, Guid jobId)
    {
        await using var db = factory.NewActiveContext();
        var well = new Well(jobId, "Lone Star 14H", WellType.Target);
        db.Wells.Add(well);
        await db.SaveChangesAsync();
        return well.Id;
    }

    private static async Task<int> SeedTieOnAsync(
        FakeTenantDbContextFactory factory, int wellId, double depth = 1000)
    {
        await using var db = factory.NewActiveContext();
        var tieOn = new TieOn(wellId, depth, inclination: 5, azimuth: 90);
        db.TieOns.Add(tieOn);
        await db.SaveChangesAsync();
        return tieOn.Id;
    }

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedTypeSuffix)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.EndsWith(expectedTypeSuffix, problem.Type);
    }

    [Fact]
    public async Task List_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.List(jobId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task List_KnownWellNoTieOns_ReturnsEmpty()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<TieOnSummaryDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task List_ReturnsTieOnsOrderedByDepth()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await SeedTieOnAsync(factory, wellId, depth: 3000);
        await SeedTieOnAsync(factory, wellId, depth: 1000);
        await SeedTieOnAsync(factory, wellId, depth: 2000);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<TieOnSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.Depth));
    }

    [Fact]
    public async Task List_WellUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobA   = await SeedJobAsync(factory);
        var jobB   = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);

        AssertProblem(await sut.List(jobB, wellId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var wellId  = await SeedWellAsync(factory, jobId);
        var tieOnId = await SeedTieOnAsync(factory, wellId, depth: 1500);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(jobId, wellId, tieOnId, CancellationToken.None));
        var dto = Assert.IsType<TieOnDetailDto>(ok.Value);
        Assert.Equal(1500d, dto.Depth);
    }

    [Fact]
    public async Task Get_UnknownTieOn_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Get(jobId, wellId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateTieOnDto(
                Depth: 1234, Inclination: 12, Azimuth: 180,
                North: 100, East: 200, Northing: 1100, Easting: 2200,
                VerticalReference: 1230, SubSeaReference: 50,
                VerticalSectionDirection: 90),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<TieOnSummaryDto>(created.Value);
        Assert.Equal(1234d, summary.Depth);

        await using var db = factory.NewActiveContext();
        var stored = await db.TieOns.AsNoTracking().FirstAsync();
        Assert.Equal("system", stored.CreatedBy);
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(await sut.Create(jobId, 99999,
            new CreateTieOnDto(0, 0, 0),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var wellId  = await SeedWellAsync(factory, jobId);
        var tieOnId = await SeedTieOnAsync(factory, wellId);

        var result = await sut.Update(jobId, wellId, tieOnId,
            new UpdateTieOnDto(
                Depth: 2000, Inclination: 30, Azimuth: 270,
                North: 1, East: 2, Northing: 3, Easting: 4,
                VerticalReference: 5, SubSeaReference: 6,
                VerticalSectionDirection: 7),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.TieOns.AsNoTracking().FirstAsync(t => t.Id == tieOnId);
        Assert.Equal(2000d, reloaded.Depth);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Update_UnknownTieOn_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId, 99999,
            new UpdateTieOnDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_ResetsToZero()
    {
        var (sut, factory) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var wellId  = await SeedWellAsync(factory, jobId);
        // SeedTieOnAsync seeds depth=1000, inc=5, az=90 — non-zero,
        // so the reset is observable.
        var tieOnId = await SeedTieOnAsync(factory, wellId);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, tieOnId, CancellationToken.None));

        // Row stays — every Well must keep a tie-on on file (Marduk's
        // calc requires an anchor; without one, recalc no-ops). Every
        // observed and reference field is zeroed so subsequent surveys
        // compute against an all-zero anchor.
        await using var db = factory.NewActiveContext();
        var reloaded = await db.TieOns.AsNoTracking().FirstAsync(t => t.Id == tieOnId);
        Assert.Equal(0d, reloaded.Depth);
        Assert.Equal(0d, reloaded.Inclination);
        Assert.Equal(0d, reloaded.Azimuth);
        Assert.Equal(0d, reloaded.North);
        Assert.Equal(0d, reloaded.East);
        Assert.Equal(0d, reloaded.Northing);
        Assert.Equal(0d, reloaded.Easting);
        Assert.Equal(0d, reloaded.VerticalReference);
        Assert.Equal(0d, reloaded.SubSeaReference);
        Assert.Equal(0d, reloaded.VerticalSectionDirection);
    }

    [Fact]
    public async Task Delete_UnknownTieOn_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Delete(jobId, wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
