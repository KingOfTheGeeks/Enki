using AMR.Core.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Surveys;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;
using System.Text;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class SurveysControllerTests
{
    private static (SurveysController Controller, FakeTenantDbContextFactory Factory, FakeSurveyCalculator Calculator) NewSut()
    {
        // Wrap the FakeSurveyCalculator in the real MardukSurveyAutoCalculator
        // so tests exercise the production auto-calc wiring (load → run →
        // writeback → save) against a deterministic fake Marduk. Tighter
        // than stubbing ISurveyAutoCalculator outright. The real
        // SurveyImporter is stateless so we use it directly — there's no
        // value in faking AMR.Core.IO; that's its own test surface.
        var factory    = new FakeTenantDbContextFactory();
        var calculator = new FakeSurveyCalculator();
        var autoCalc   = new MardukSurveyAutoCalculator(calculator);
        var importer   = new SurveyImporter();
        var controller = new SurveysController(factory, autoCalc, importer)
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
        var well = new Well(jobId, "Lone Star 14H", WellType.Target);
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
    public async Task Create_DepthEqualsTieOnDepth_ReturnsValidationProblem()
    {
        // Reproduces the production failure mode: every well auto-gets
        // a tie-on at depth=0 (WellsController.Create), and the New
        // Survey form defaults Depth to 0 too. Without this gate,
        // RecalculateAsync runs MinimumCurvature with deltaMd = 0 and
        // the SaveChanges blows up on TDS RPC. Server now refuses
        // up-front with a clean 400.
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAsync(factory, wellId);    // depth = 0

        AssertProblem(await sut.Create(jobId, wellId,
            new CreateSurveyDto(Depth: 0, Inclination: 0, Azimuth: 0),
            CancellationToken.None), 400, "/validation");

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Surveys.CountAsync(s => s.WellId == wellId));
    }

    [Fact]
    public async Task Create_DepthEqualsExistingSurveyDepth_ReturnsValidationProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedSurveyAsync(factory, wellId, depth: 1500);

        AssertProblem(await sut.Create(jobId, wellId,
            new CreateSurveyDto(Depth: 1500, Inclination: 5, Azimuth: 180),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task Create_DepthBetweenExistingSurveys_Succeeds()
    {
        // Drilling reality: a station at depth between two existing
        // ones is a normal infill (driller missed taking a survey at
        // 1500, going back to fill it in). Marduk's engine sorts by
        // depth internally so insertion order doesn't matter — only
        // uniqueness does.
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedSurveyAsync(factory, wellId, depth: 1000);
        await SeedSurveyAsync(factory, wellId, depth: 2000);

        var result = await sut.Create(jobId, wellId,
            new CreateSurveyDto(Depth: 1500, Inclination: 5, Azimuth: 180),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
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
    public async Task CreateBulk_DuplicatesExistingSurveyDepth_ReturnsValidationProblem()
    {
        // Mirrors the singleton Create gate but at the bulk endpoint:
        // a batch that's monotonic within itself but collides with an
        // already-saved survey is also rejected.
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedSurveyAsync(factory, wellId, depth: 2000);

        AssertProblem(await sut.CreateBulk(jobId, wellId,
            new CreateSurveysDto(new[]
            {
                new CreateSurveyDto(1000, 0, 0),
                new CreateSurveyDto(2000, 5, 90),  // collides with existing
                new CreateSurveyDto(3000, 10, 180),
            }),
            CancellationToken.None), 400, "/validation");

        // Atomic: nothing inserted on the failure path.
        await using var db = factory.NewActiveContext();
        Assert.Equal(1, await db.Surveys.CountAsync(s => s.WellId == wellId));
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
    public async Task Update_DepthEqualsAnotherStation_ReturnsValidationProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId    = await SeedJobAsync(factory);
        var wellId   = await SeedWellAsync(factory, jobId);
        var firstId  = await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);

        // Try to move the first survey's depth to collide with the
        // second one's depth — must be refused.
        AssertProblem(await sut.Update(jobId, wellId, firstId,
            new UpdateSurveyDto(Depth: 2000, Inclination: 5, Azimuth: 90),
            CancellationToken.None), 400, "/validation");

        // First survey unchanged.
        await using var db = factory.NewActiveContext();
        var reloaded = await db.Surveys.AsNoTracking().FirstAsync(s => s.Id == firstId);
        Assert.Equal(1000d, reloaded.Depth);
    }

    [Fact]
    public async Task Update_DepthUnchanged_Succeeds()
    {
        // The exclude-self path: editing a survey without moving its
        // Depth (e.g. just changing Inclination) must not 400 because
        // the survey's own Depth is in the existing-set.
        var (sut, factory, _) = NewSut();
        var jobId    = await SeedJobAsync(factory);
        var wellId   = await SeedWellAsync(factory, jobId);
        var surveyId = await SeedSurveyAsync(factory, wellId, 1500);

        var result = await sut.Update(jobId, wellId, surveyId,
            new UpdateSurveyDto(Depth: 1500, Inclination: 12, Azimuth: 200),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
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

    // ---------- import ----------

    /// <summary>
    /// Builds an <see cref="IFormFile"/> from an in-memory CSV / LAS string —
    /// avoids spinning up a real multipart request for the import-action tests.
    /// </summary>
    private static IFormFile MakeFormFile(string content, string fileName = "test.csv")
    {
        var bytes  = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "text/csv",
        };
    }

    [Fact]
    public async Task Import_ValidCsv_ReplacesSurveysAndReturnsResult()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAsync(factory, wellId);

        // Seed a stale survey that should be replaced by the import.
        await SeedSurveyAsync(factory, wellId, 9999);

        var csv = "MD,Inc,Azi\n0,0,0\n100,1,90\n200,2,180\n300,3,270\n";
        var file = MakeFormFile(csv);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.Import(jobId, wellId, file, keepExistingTieOn: false, CancellationToken.None));
        var result = Assert.IsType<SurveyImportResultDto>(ok.Value);

        // Depth-0 row was promoted to the tie-on by the importer, so 3
        // surveys land in the DB (100, 200, 300) and one tie-on is
        // recreated from the file.
        Assert.Equal(3, result.SurveysImported);
        Assert.Equal(1, result.TieOnsCreated);
        Assert.Equal("Csv", result.DetectedFormat);

        await using var db = factory.NewActiveContext();
        var rows = await db.Surveys.AsNoTracking().OrderBy(s => s.Depth).ToListAsync();
        Assert.Equal(new[] { 100d, 200d, 300d }, rows.Select(r => r.Depth));
    }

    [Fact]
    public async Task Import_KeepExistingTieOn_PreservesCuratedTieOn()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        // Seed a tie-on with a non-default Northing — the value should
        // survive the import unchanged when keepExistingTieOn is true.
        await using (var seed = factory.NewActiveContext())
        {
            seed.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing = 12345,
            });
            await seed.SaveChangesAsync();
        }

        var csv = "MD,Inc,Azi\n0,0,0\n100,1,90\n200,2,180\n";
        var file = MakeFormFile(csv);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.Import(jobId, wellId, file, keepExistingTieOn: true, CancellationToken.None));
        var result = Assert.IsType<SurveyImportResultDto>(ok.Value);

        Assert.Equal(0, result.TieOnsCreated);   // existing tie-on preserved

        await using var db = factory.NewActiveContext();
        var tieOn = await db.TieOns.AsNoTracking().FirstAsync(t => t.WellId == wellId);
        Assert.Equal(12345, tieOn.Northing);     // curated value intact
    }

    [Fact]
    public async Task Import_NonDefaultExistingTieOn_NoKeepFlag_Returns409Conflict()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        // A tie-on with any non-zero value counts as "non-default" and
        // must not be silently overwritten — the controller should
        // refuse and demand an explicit keepExistingTieOn flag.
        await using (var seed = factory.NewActiveContext())
        {
            seed.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing = 12345,
            });
            await seed.SaveChangesAsync();
        }

        var csv  = "MD,Inc,Azi\n0,0,0\n100,1,90\n200,2,180\n";
        var file = MakeFormFile(csv);

        var result = await sut.Import(jobId, wellId, file, keepExistingTieOn: null, CancellationToken.None);
        AssertProblem(result, 409, "/conflict");

        // Nothing committed when the gate fired — surveys untouched.
        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Surveys.CountAsync(s => s.WellId == wellId));
        var tieOn = await db.TieOns.AsNoTracking().FirstAsync(t => t.WellId == wellId);
        Assert.Equal(12345, tieOn.Northing);
    }

    [Fact]
    public async Task Import_NonDefaultExistingTieOn_KeepFalse_OverwritesAndProceeds()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var seed = factory.NewActiveContext())
        {
            seed.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing = 12345,
            });
            await seed.SaveChangesAsync();
        }

        var csv  = "MD,Inc,Azi\n0,0,0\n100,1,90\n200,2,180\n";
        var file = MakeFormFile(csv);

        Assert.IsType<OkObjectResult>(
            await sut.Import(jobId, wellId, file, keepExistingTieOn: false, CancellationToken.None));

        // Explicit "overwrite" → existing tie-on replaced by the
        // file's (Northing back to 0).
        await using var db = factory.NewActiveContext();
        var tieOn = await db.TieOns.AsNoTracking().FirstAsync(t => t.WellId == wellId);
        Assert.Equal(0, tieOn.Northing);
    }

    [Fact]
    public async Task Import_GibberishFile_ReturnsValidationProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var file = MakeFormFile("this isn't a survey file at all\nnope\n", "junk.txt");

        AssertProblem(await sut.Import(jobId, wellId, file, keepExistingTieOn: false, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Import_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var file = MakeFormFile("MD,Inc,Azi\n0,0,0\n100,1,90\n");

        AssertProblem(await sut.Import(jobId, 99999, file, keepExistingTieOn: false, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Import_EmptyFile_ReturnsValidationProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var file = MakeFormFile("");

        AssertProblem(await sut.Import(jobId, wellId, file, keepExistingTieOn: false, CancellationToken.None),
            400, "/validation");
    }

    // ---------- delete-all (Clear) ----------

    [Fact]
    public async Task DeleteAll_RemovesEverySurveyOnWell_LeavesTieOnAlone()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedTieOnAsync(factory, wellId);
        await SeedSurveyAsync(factory, wellId, 1000);
        await SeedSurveyAsync(factory, wellId, 2000);
        await SeedSurveyAsync(factory, wellId, 3000);

        Assert.IsType<NoContentResult>(
            await sut.DeleteAll(jobId, wellId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Surveys.CountAsync(s => s.WellId == wellId));
        // Tie-on survives — Clear is scoped to surveys only.
        Assert.Equal(1, await db.TieOns.CountAsync(t => t.WellId == wellId));
    }

    [Fact]
    public async Task DeleteAll_OnEmptyCollection_ReturnsNoContent()
    {
        var (sut, factory, _) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        // No surveys seeded — DELETE on empty collection is idempotent.
        Assert.IsType<NoContentResult>(
            await sut.DeleteAll(jobId, wellId, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAll_ScopedToWell_LeavesOtherWellsUntouched()
    {
        var (sut, factory, _) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var wellA   = await SeedWellAsync(factory, jobId);
        var wellB   = await SeedWellAsync(factory, jobId);
        await SeedSurveyAsync(factory, wellA, 1000);
        await SeedSurveyAsync(factory, wellB, 1000);

        await sut.DeleteAll(jobId, wellA, CancellationToken.None);

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Surveys.CountAsync(s => s.WellId == wellA));
        Assert.Equal(1, await db.Surveys.CountAsync(s => s.WellId == wellB));
    }

    [Fact]
    public async Task DeleteAll_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory, _) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(await sut.DeleteAll(jobId, 99999, CancellationToken.None),
            404, "/not-found");
    }
}
