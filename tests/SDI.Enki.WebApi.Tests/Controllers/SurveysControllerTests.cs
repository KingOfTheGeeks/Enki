using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class SurveysControllerTests
{
    private static (SurveysController Controller, FakeTenantDbContextFactory Factory, FakeSurveyCalculator Calculator) NewSut()
    {
        var factory    = new FakeTenantDbContextFactory();
        var calculator = new FakeSurveyCalculator();
        var controller = new SurveysController(factory, calculator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
                RouteData   = new RouteData
                {
                    Values = { ["tenantCode"] = "TENANTTEST" },
                },
            },
        };
        return (controller, factory, calculator);
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
        var well = new Well(jobId, "Johnson 1H", WellType.Target);
        db.Wells.Add(well);
        await db.SaveChangesAsync();
        return well.Id;
    }

    private static async Task SeedTieOnAsync(FakeTenantDbContextFactory factory, int wellId)
    {
        await using var db = factory.NewActiveContext();
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0));
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedSurveyAsync(
        FakeTenantDbContextFactory factory, int wellId, double depth)
    {
        await using var db = factory.NewActiveContext();
        var s = new Survey(wellId, depth, inclination: 5, azimuth: 90);
        db.Surveys.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
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
        var (sut, factory, _) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.List(jobId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task List_ReturnsSurveysOrderedByDepth()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await SeedSurveyAsync(factory, wellId, 3000);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<SurveySummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.Depth));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory, _) = NewSut();
        var jobId    = await SeedJobAsync(factory);
        var wellId   = await SeedWellAsync(factory, jobId);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1500);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(jobId, wellId, surveyId, CancellationToken.None));
        var dto = Assert.IsType<SurveyDetailDto>(ok.Value);
        Assert.Equal(1500d, dto.Depth);
    }

    [Fact]
    public async Task Get_WellUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobA   = await SeedJobAsync(factory);
        var jobB   = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1000);

        AssertProblem(await sut.Get(jobB, wellId, surveyId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateSurveyDto(Depth: 2500, Inclination: 30, Azimuth: 180),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<SurveySummaryDto>(created.Value);
        Assert.Equal(2500d, summary.Depth);
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.Create(jobId, 99999,
            new CreateSurveyDto(0, 0, 0),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task CreateBulk_MonotonicDepth_InsertsAllAtomically()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.CreateBulk(jobId, wellId,
            new CreateSurveysDto(new[]
            {
                new CreateSurveyDto(1000, 0, 0),
                new CreateSurveyDto(2000, 5, 90),
                new CreateSurveyDto(3000, 10, 180),
            }),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = ((IEnumerable<SurveySummaryDto>)ok.Value!).ToList();
        Assert.Equal(3, rows.Count);

        await using var db = factory.NewActiveContext();
        Assert.Equal(3, await db.Surveys.CountAsync());
    }

    [Fact]
    public async Task CreateBulk_NonMonotonicDepth_ReturnsValidationProblemAndInsertsNothing()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.CreateBulk(jobId, wellId,
            new CreateSurveysDto(new[]
            {
                new CreateSurveyDto(1000, 0, 0),
                new CreateSurveyDto(2000, 5, 90),
                new CreateSurveyDto(1500, 10, 180),
            }),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Surveys.CountAsync());
    }

    [Fact]
    public async Task CreateBulk_DuplicateDepth_ReturnsValidationProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.CreateBulk(jobId, wellId,
            new CreateSurveysDto(new[]
            {
                new CreateSurveyDto(1000, 0, 0),
                new CreateSurveyDto(1000, 5, 90),
            }),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task CreateBulk_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(await sut.CreateBulk(jobId, 99999,
            new CreateSurveysDto(new[] { new CreateSurveyDto(1000, 0, 0) }),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_RewritesObservedFieldsAndStampsUpdated()
    {
        var (sut, factory, _) = NewSut();
        var jobId    = await SeedJobAsync(factory);
        var wellId   = await SeedWellAsync(factory, jobId);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1000);

        var result = await sut.Update(jobId, wellId, surveyId,
            new UpdateSurveyDto(Depth: 1100, Inclination: 12, Azimuth: 91),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Surveys.AsNoTracking().FirstAsync(s => s.Id == surveyId);
        Assert.Equal(1100d, reloaded.Depth);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Update_UnknownSurvey_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId, 99999,
            new UpdateSurveyDto(0, 0, 0),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory, _) = NewSut();
        var jobId    = await SeedJobAsync(factory);
        var wellId   = await SeedWellAsync(factory, jobId);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1000);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, surveyId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Surveys.AnyAsync(s => s.Id == surveyId));
    }

    [Fact]
    public async Task Calculate_NoTieOn_ReturnsValidationProblem()
    {
        var (sut, factory, calculator) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedSurveyAsync(factory, wellId, 1000);

        var result = await sut.Calculate(jobId, wellId,
            new SurveyCalculationRequestDto(),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        Assert.Equal(0, calculator.CallCount);
    }

    [Fact]
    public async Task Calculate_NoSurveys_ReturnsZeroProcessed()
    {
        var (sut, factory, calculator) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAsync(factory, wellId);

        var ok = Assert.IsType<OkObjectResult>(await sut.Calculate(jobId, wellId,
            new SurveyCalculationRequestDto(), CancellationToken.None));
        var dto = Assert.IsType<SurveyCalculationResponseDto>(ok.Value);
        Assert.Equal(0, dto.SurveysProcessed);
        Assert.Equal(0, calculator.CallCount);
    }

    [Fact]
    public async Task Calculate_HappyPath_WritesComputedFieldsBack()
    {
        var (sut, factory, calculator) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAsync(factory, wellId);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);
        await SeedSurveyAsync(factory, wellId, 3000);

        var ok = Assert.IsType<OkObjectResult>(await sut.Calculate(jobId, wellId,
            new SurveyCalculationRequestDto(), CancellationToken.None));
        var dto = Assert.IsType<SurveyCalculationResponseDto>(ok.Value);
        Assert.Equal(3, dto.SurveysProcessed);
        Assert.Equal(1, calculator.CallCount);

        await using var db = factory.NewActiveContext();
        var rows = await db.Surveys.AsNoTracking().OrderBy(s => s.Depth).ToListAsync();
        Assert.All(rows, r => Assert.NotEqual(0d, r.VerticalDepth));
    }

    [Fact]
    public async Task Calculate_LengthMismatch_ThrowsAndDoesNotPersist()
    {
        var (sut, factory, calculator) = NewSut();
        calculator.ReturnShorter = true;
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAsync(factory, wellId);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Calculate(jobId, wellId, new SurveyCalculationRequestDto(), CancellationToken.None));

        await using var db = factory.NewActiveContext();
        var rows = await db.Surveys.AsNoTracking().ToListAsync();
        Assert.All(rows, r => Assert.Equal(0d, r.VerticalDepth));
    }
}
