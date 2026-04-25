using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="SurveysController"/>. CRUD
/// happy paths + the bulk-create monotonicity guard + the Calculate
/// wiring (using <see cref="FakeSurveyCalculator"/> so the real
/// Marduk math doesn't need a live wire — it's tested in Marduk).
/// </summary>
public class SurveysControllerTests
{
    // ---------- fixture helpers ----------

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

    private static async Task<int> SeedWellAsync(FakeTenantDbContextFactory factory)
    {
        await using var db = factory.NewActiveContext();
        var well = new Well("Johnson 1H", WellType.Target);
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

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _, _) = NewSut();
        var result = await sut.List(99999, CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task List_ReturnsSurveysOrderedByDepth()
    {
        var (sut, factory, _) = NewSut();
        var wellId = await SeedWellAsync(factory);

        // Insert out of order to prove the ORDER BY actually fires.
        await SeedSurveyAsync(factory, wellId, 3000);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);

        var result = await sut.List(wellId, CancellationToken.None);

        var ok   = Assert.IsType<OkObjectResult>(result);
        var rows = ((IEnumerable<SurveySummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.Depth));
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory, _) = NewSut();
        var wellId   = await SeedWellAsync(factory);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1500);

        var result = await sut.Get(wellId, surveyId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SurveyDetailDto>(ok.Value);
        Assert.Equal(surveyId, dto.Id);
        Assert.Equal(1500d, dto.Depth);
    }

    [Fact]
    public async Task Get_SurveyUnderDifferentWell_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var wellA = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other", WellType.Offset));
            await db.SaveChangesAsync();
        }
        var surveyId = await SeedSurveyAsync(factory, wellA, 1000);

        var result = await sut.Get(wellId: 2, surveyId, CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Create one
    // ============================================================

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory, _) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateSurveyDto(Depth: 2500, Inclination: 30, Azimuth: 180),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<SurveySummaryDto>(created.Value);
        Assert.Equal(2500d, summary.Depth);
        Assert.Equal(0d, summary.VerticalDepth);   // not yet calculated

        await using var db = factory.NewActiveContext();
        Assert.Single(await db.Surveys.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _, _) = NewSut();
        var result = await sut.Create(99999,
            new CreateSurveyDto(0, 0, 0),
            CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Bulk create
    // ============================================================

    [Fact]
    public async Task CreateBulk_MonotonicDepth_InsertsAllAtomically()
    {
        var (sut, factory, _) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.CreateBulk(wellId,
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
        var wellId = await SeedWellAsync(factory);

        var result = await sut.CreateBulk(wellId,
            new CreateSurveysDto(new[]
            {
                new CreateSurveyDto(1000, 0, 0),
                new CreateSurveyDto(2000, 5, 90),
                new CreateSurveyDto(1500, 10, 180),   // out of order
            }),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");

        // Atomic: no rows persisted on failure.
        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Surveys.CountAsync());
    }

    [Fact]
    public async Task CreateBulk_DuplicateDepth_ReturnsValidationProblem()
    {
        var (sut, factory, _) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.CreateBulk(wellId,
            new CreateSurveysDto(new[]
            {
                new CreateSurveyDto(1000, 0, 0),
                new CreateSurveyDto(1000, 5, 90),   // duplicate
            }),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task CreateBulk_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _, _) = NewSut();

        var result = await sut.CreateBulk(99999,
            new CreateSurveysDto(new[] { new CreateSurveyDto(1000, 0, 0) }),
            CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Update / Delete
    // ============================================================

    [Fact]
    public async Task Update_RewritesObservedFieldsAndStampsUpdated()
    {
        var (sut, factory, _) = NewSut();
        var wellId   = await SeedWellAsync(factory);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1000);

        var result = await sut.Update(wellId, surveyId,
            new UpdateSurveyDto(Depth: 1100, Inclination: 12, Azimuth: 91),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Surveys.AsNoTracking().FirstAsync(s => s.Id == surveyId);
        Assert.Equal(1100d, reloaded.Depth);
        Assert.Equal(12d, reloaded.Inclination);
        Assert.Equal(91d, reloaded.Azimuth);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Update_UnknownSurvey_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Update(wellId, 99999,
            new UpdateSurveyDto(0, 0, 0),
            CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory, _) = NewSut();
        var wellId   = await SeedWellAsync(factory);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1000);

        var result = await sut.Delete(wellId, surveyId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await using var db = factory.NewActiveContext();
        Assert.False(await db.Surveys.AnyAsync(s => s.Id == surveyId));
    }

    // ============================================================
    // Calculate
    // ============================================================

    [Fact]
    public async Task Calculate_NoTieOn_ReturnsValidationProblem()
    {
        var (sut, factory, calculator) = NewSut();
        var wellId = await SeedWellAsync(factory);
        await SeedSurveyAsync(factory, wellId, 1000);

        var result = await sut.Calculate(wellId,
            new SurveyCalculationRequestDto(),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        Assert.Equal(0, calculator.CallCount);
    }

    [Fact]
    public async Task Calculate_NoSurveys_ReturnsZeroProcessed()
    {
        var (sut, factory, calculator) = NewSut();
        var wellId = await SeedWellAsync(factory);
        await SeedTieOnAsync(factory, wellId);

        var result = await sut.Calculate(wellId,
            new SurveyCalculationRequestDto(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SurveyCalculationResponseDto>(ok.Value);
        Assert.Equal(0, dto.SurveysProcessed);
        Assert.Equal(0, calculator.CallCount);   // no calc when input is empty
    }

    [Fact]
    public async Task Calculate_HappyPath_WritesComputedFieldsBack()
    {
        var (sut, factory, calculator) = NewSut();
        var wellId = await SeedWellAsync(factory);
        await SeedTieOnAsync(factory, wellId);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);
        await SeedSurveyAsync(factory, wellId, 3000);

        var result = await sut.Calculate(wellId,
            new SurveyCalculationRequestDto(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SurveyCalculationResponseDto>(ok.Value);
        Assert.Equal(3, dto.SurveysProcessed);
        Assert.Equal(1, calculator.CallCount);

        // FakeSurveyCalculator stamps deterministic non-zero values
        // index-aligned with input — VerticalDepth = depth * 0.95 etc.
        await using var db = factory.NewActiveContext();
        var rows = await db.Surveys.AsNoTracking().OrderBy(s => s.Depth).ToListAsync();
        Assert.All(rows, r => Assert.NotEqual(0d, r.VerticalDepth));
        Assert.NotEqual(0d, rows[0].DoglegSeverity);
    }

    [Fact]
    public async Task Calculate_LengthMismatch_ThrowsAndDoesNotPersist()
    {
        var (sut, factory, calculator) = NewSut();
        calculator.ReturnShorter = true;
        var wellId = await SeedWellAsync(factory);
        await SeedTieOnAsync(factory, wellId);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Calculate(wellId, new SurveyCalculationRequestDto(), CancellationToken.None));

        // Computed columns must remain untouched on the failure path.
        await using var db = factory.NewActiveContext();
        var rows = await db.Surveys.AsNoTracking().ToListAsync();
        Assert.All(rows, r => Assert.Equal(0d, r.VerticalDepth));
    }
}
