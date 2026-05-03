using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class CommonMeasuresControllerTests
{
    private static (CommonMeasuresController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var resolver = new SurveyTvdResolver(new FakeSurveyInterpolator());
        var controller = new CommonMeasuresController(factory, resolver)
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

    /// <summary>
    /// Tie-on (depth 0) + one survey at <paramref name="maxMd"/>. Default
    /// 10 000 brackets every interval used in this fixture.
    /// </summary>
    private static async Task SeedTieOnAndSurveysAsync(
        FakeTenantDbContextFactory factory, int wellId, double maxMd = 10_000)
    {
        await using var db = factory.NewActiveContext();
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0));
        db.Surveys.Add(new Survey(wellId, depth: maxMd, inclination: 0, azimuth: 0));
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedMeasureAsync(
        FakeTenantDbContextFactory factory, int wellId,
        double fromMd = 0, double toMd = 1000, double value = 10)
    {
        await using var db = factory.NewActiveContext();
        var c = new CommonMeasure(wellId, fromMd, toMd, value)
        {
            RowVersion = TestRowVersionBytes,
        };
        db.CommonMeasures.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
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
    public async Task List_ReturnsMeasuresOrderedByFromMeasured()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedMeasureAsync(factory, wellId, fromMd: 3000, toMd: 4000);
        await SeedMeasureAsync(factory, wellId, fromMd: 1000, toMd: 2000);
        await SeedMeasureAsync(factory, wellId, fromMd: 2000, toMd: 3000);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<CommonMeasureSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.FromMeasured));
    }

    [Fact]
    public async Task List_WhenWellHasTieOnAndSurveys_ProjectsDerivedTvds()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAndSurveysAsync(factory, wellId);
        await SeedMeasureAsync(factory, wellId, fromMd: 1000, toMd: 2000);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<CommonMeasureSummaryDto>)ok.Value!).ToList();
        Assert.Equal(1000 * 0.95, rows[0].FromTvd);
        Assert.Equal(2000 * 0.95, rows[0].ToTvd);
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
        await SeedTieOnAndSurveysAsync(factory, wellId);

        var result = await sut.Create(jobId, wellId,
            new CreateCommonMeasureDto(FromMeasured: 1000, ToMeasured: 2000, Value: 12.5),
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
        await SeedTieOnAndSurveysAsync(factory, wellId);

        var result = await sut.Create(jobId, wellId,
            new CreateCommonMeasureDto(FromMeasured: 2000, ToMeasured: 1000, Value: 1),
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
    public async Task Create_WellWithoutSurveys_ReturnsConflictProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Create(jobId, wellId,
            new CreateCommonMeasureDto(0, 100, 1),
            CancellationToken.None), 409, "/conflict");
    }

    [Fact]
    public async Task Create_MdOutsideSurveyRange_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAndSurveysAsync(factory, wellId, maxMd: 5000);

        AssertProblem(await sut.Create(jobId, wellId,
            new CreateCommonMeasureDto(6000, 7000, 1),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAndSurveysAsync(factory, wellId);
        var mId    = await SeedMeasureAsync(factory, wellId);

        var result = await sut.Update(jobId, wellId, mId,
            new UpdateCommonMeasureDto(FromMeasured: 500, ToMeasured: 1500, Value: 22, RowVersion: TestRowVersion),
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
        await SeedTieOnAndSurveysAsync(factory, wellId);
        var mId    = await SeedMeasureAsync(factory, wellId);

        AssertProblem(await sut.Update(jobId, wellId, mId,
            new UpdateCommonMeasureDto(2000, 1000, 1, RowVersion: TestRowVersion),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownMeasure_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAndSurveysAsync(factory, wellId);

        AssertProblem(await sut.Update(jobId, wellId, 99999,
            new UpdateCommonMeasureDto(0, 100, 1, RowVersion: TestRowVersion),
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
