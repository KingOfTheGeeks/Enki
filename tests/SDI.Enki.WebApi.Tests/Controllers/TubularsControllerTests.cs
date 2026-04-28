using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Wells.Tubulars;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class TubularsControllerTests
{
    private static (TubularsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new TubularsController(factory)
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

    private static async Task<int> SeedTubularAsync(
        FakeTenantDbContextFactory factory, int wellId, int order, TubularType? type = null)
    {
        await using var db = factory.NewActiveContext();
        var t = new Tubular(wellId, order, type ?? TubularType.Casing,
            fromMeasured: 0, toMeasured: 1000, diameter: 9.625, weight: 47)
        {
            RowVersion = TestRowVersionBytes,
        };
        db.Tubulars.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

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
    public async Task List_ReturnsTubularsOrderedByOrder()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTubularAsync(factory, wellId, order: 2);
        await SeedTubularAsync(factory, wellId, order: 0);
        await SeedTubularAsync(factory, wellId, order: 1);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<TubularSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(r => r.Order));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var jobId     = await SeedJobAsync(factory);
        var wellId    = await SeedWellAsync(factory, jobId);
        var tubularId = await SeedTubularAsync(factory, wellId, order: 0, type: TubularType.Liner);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.Get(jobId, wellId, tubularId, CancellationToken.None));
        var dto = Assert.IsType<TubularDetailDto>(ok.Value);
        Assert.Equal("Liner", dto.Type);
    }

    [Fact]
    public async Task Get_TubularUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobA      = await SeedJobAsync(factory);
        var jobB      = await SeedJobAsync(factory);
        var wellId    = await SeedWellAsync(factory, jobA);
        var tubularId = await SeedTubularAsync(factory, wellId, 0);

        AssertProblem(await sut.Get(jobB, wellId, tubularId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateTubularDto(
                Type: "Casing", Order: 0,
                FromMeasured: 0, ToMeasured: 5000,
                Diameter: 13.375, Weight: 68,
                Name: "Surface casing"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<TubularSummaryDto>(created.Value);
        Assert.Equal("Casing", summary.Type);
    }

    [Fact]
    public async Task Create_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateTubularDto("Bogus", 0, 0, 100, 9.625, 47),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(await sut.Create(jobId, 99999,
            new CreateTubularDto("Casing", 0, 0, 100, 9.625, 47),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var jobId     = await SeedJobAsync(factory);
        var wellId    = await SeedWellAsync(factory, jobId);
        var tubularId = await SeedTubularAsync(factory, wellId, 0, TubularType.Casing);

        var result = await sut.Update(jobId, wellId, tubularId,
            new UpdateTubularDto(
                Type: "DrillPipe", Order: 5,
                FromMeasured: 100, ToMeasured: 9000,
                Diameter: 5.0, Weight: 19.5,
                Name: "DP string",
                RowVersion: TestRowVersion),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Tubulars.AsNoTracking().FirstAsync(t => t.Id == tubularId);
        Assert.Equal(TubularType.DrillPipe, reloaded.Type);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Update_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId     = await SeedJobAsync(factory);
        var wellId    = await SeedWellAsync(factory, jobId);
        var tubularId = await SeedTubularAsync(factory, wellId, 0);

        AssertProblem(await sut.Update(jobId, wellId, tubularId,
            new UpdateTubularDto("NotAType", 0, 0, 100, 9.625, 47, null, RowVersion: TestRowVersion),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownTubular_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId, 99999,
            new UpdateTubularDto("Casing", 0, 0, 100, 9.625, 47, null, RowVersion: TestRowVersion),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var jobId     = await SeedJobAsync(factory);
        var wellId    = await SeedWellAsync(factory, jobId);
        var tubularId = await SeedTubularAsync(factory, wellId, 0);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, tubularId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Tubulars.AnyAsync(t => t.Id == tubularId));
    }

    [Fact]
    public async Task Delete_UnknownTubular_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Delete(jobId, wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
