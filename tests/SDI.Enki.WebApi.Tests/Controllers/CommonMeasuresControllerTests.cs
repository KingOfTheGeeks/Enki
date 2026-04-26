using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class CommonMeasuresControllerTests
{
    private static (CommonMeasuresController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new CommonMeasuresController(factory)
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

    private static async Task<int> SeedMeasureAsync(
        FakeTenantDbContextFactory factory, int wellId,
        double fromV = 0, double toV = 1000, double value = 10)
    {
        await using var db = factory.NewActiveContext();
        var c = new CommonMeasure(wellId, fromV, toV, value);
        db.CommonMeasures.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
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
    public async Task List_ReturnsMeasuresOrderedByFromVertical()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedMeasureAsync(factory, wellId, fromV: 3000, toV: 4000);
        await SeedMeasureAsync(factory, wellId, fromV: 1000, toV: 2000);
        await SeedMeasureAsync(factory, wellId, fromV: 2000, toV: 3000);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<CommonMeasureSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.FromVertical));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var mId    = await SeedMeasureAsync(factory, wellId, value: 9.5);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(jobId, wellId, mId, CancellationToken.None));
        var dto = Assert.IsType<CommonMeasureDetailDto>(ok.Value);
        Assert.Equal(9.5d, dto.Value);
    }

    [Fact]
    public async Task Get_MeasureUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobA   = await SeedJobAsync(factory);
        var jobB   = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);
        var mId    = await SeedMeasureAsync(factory, wellId);

        AssertProblem(await sut.Get(jobB, wellId, mId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateCommonMeasureDto(FromVertical: 1000, ToVertical: 2000, Value: 12.5),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<CommonMeasureSummaryDto>(created.Value);
        Assert.Equal(12.5d, summary.Value);
    }

    [Fact]
    public async Task Create_FromGreaterThanTo_ReturnsValidationProblemAndPersistsNothing()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateCommonMeasureDto(FromVertical: 2000, ToVertical: 1000, Value: 1),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.CommonMeasures.CountAsync());
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(await sut.Create(jobId, 99999,
            new CreateCommonMeasureDto(0, 100, 1),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var mId    = await SeedMeasureAsync(factory, wellId);

        var result = await sut.Update(jobId, wellId, mId,
            new UpdateCommonMeasureDto(FromVertical: 500, ToVertical: 1500, Value: 22),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.CommonMeasures.AsNoTracking().FirstAsync(c => c.Id == mId);
        Assert.Equal(22d, reloaded.Value);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Update_FromGreaterThanTo_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var mId    = await SeedMeasureAsync(factory, wellId);

        AssertProblem(await sut.Update(jobId, wellId, mId,
            new UpdateCommonMeasureDto(2000, 1000, 1),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownMeasure_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId, 99999,
            new UpdateCommonMeasureDto(0, 100, 1),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var mId    = await SeedMeasureAsync(factory, wellId);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, mId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.CommonMeasures.AnyAsync(c => c.Id == mId));
    }

    [Fact]
    public async Task Delete_UnknownMeasure_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Delete(jobId, wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
