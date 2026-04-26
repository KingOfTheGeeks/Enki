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

    // =====================================================================
    // GET /tenants/{code}/jobs/{jobId}/wells/trajectories
    //
    // Aggregate projection used by the multi-well plot page. Each
    // well contributes its tie-on (if any, as the depth-0 anchor)
    // followed by every survey in MD order, all with cached
    // Northing / Easting / TVD.
    // =====================================================================

    [Fact]
    public async Task Trajectories_UnknownJob_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();

        var result = await sut.Trajectories(Guid.NewGuid(), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Trajectories_JobWithNoWells_ReturnsEmptyList()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var ok   = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Trajectories_WellWithoutTieOnOrSurveys_ReturnsRowWithEmptyPoints()
    {
        // A brand-new well shouldn't drop out of the list — the
        // chart side decides whether to skip empty curves; the API
        // returns it so the legend can still show "no data yet".
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        await SeedWellAsync(factory, jobId, "Pearson 1");

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var ok   = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(ok.Value).ToList();
        Assert.Single(rows);
        Assert.Equal("Pearson 1", rows[0].Name);
        Assert.Empty(rows[0].Points);
    }

    [Fact]
    public async Task Trajectories_WellWithTieOnOnly_ReturnsTieOnAsSinglePoint()
    {
        // Tie-on is the depth-0 anchor — must appear as the first
        // (and here, only) trajectory point. Position comes from
        // the tie-on's Northing / Easting / VerticalReference.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId, "Anchor Well");

        await using (var db = factory.NewActiveContext())
        {
            db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing          = 457_200,
                Easting           = 182_880,
                VerticalReference = 0,
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        var well = Assert.Single(rows);
        var pt = Assert.Single(well.Points);
        Assert.Equal(0,        pt.Md);
        Assert.Equal(457_200,  pt.Northing);
        Assert.Equal(182_880,  pt.Easting);
        Assert.Equal(0,        pt.Tvd);
    }

    [Fact]
    public async Task Trajectories_WellWithTieOnAndSurveys_ReturnsTieOnFirstThenSurveysByMd()
    {
        // Tie-on at depth 0 + 3 surveys at MD 100 / 200 / 300
        // (deliberately seeded out-of-order to verify the API sorts).
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing          = 1000,
                Easting           = 2000,
                VerticalReference = 0,
            });

            // Out-of-order to stress the OrderBy.
            db.Surveys.Add(new Survey(wellId, depth: 200, inclination: 0, azimuth: 0)
            { Northing = 1000, Easting = 2000, VerticalDepth = 200 });
            db.Surveys.Add(new Survey(wellId, depth: 100, inclination: 0, azimuth: 0)
            { Northing = 1000, Easting = 2000, VerticalDepth = 100 });
            db.Surveys.Add(new Survey(wellId, depth: 300, inclination: 0, azimuth: 0)
            { Northing = 1000, Easting = 2000, VerticalDepth = 300 });
            await db.SaveChangesAsync();
        }

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        var well = Assert.Single(rows);
        Assert.Equal(4, well.Points.Count);
        // Tie-on first (depth 0), then surveys in MD order.
        Assert.Equal(new[] { 0d, 100d, 200d, 300d }, well.Points.Select(p => p.Md));
        // TVD passes through too.
        Assert.Equal(new[] { 0d, 100d, 200d, 300d }, well.Points.Select(p => p.Tvd));
    }

    [Fact]
    public async Task Trajectories_MultipleWells_ReturnedInAlphabeticalOrder()
    {
        // Stable order keeps the chart legend deterministic so
        // tenants comparing screenshots see the same color-to-well
        // assignment from session to session.
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        await SeedWellAsync(factory, jobId, "Zara 9H",      WellType.Target);
        await SeedWellAsync(factory, jobId, "Adam 1",       WellType.Offset);
        await SeedWellAsync(factory, jobId, "Mike 14I",     WellType.Injection);

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        Assert.Equal(
            new[] { "Adam 1", "Mike 14I", "Zara 9H" },
            rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Trajectories_VerticalSection_ComputedFromRelativeNorthEast_AndTieOnVsd()
    {
        // V-sect is projected on the fly from each survey's
        // relative (North, East) onto the tie-on's
        // VerticalSectionDirection. Tie-on itself is the origin so
        // its V-sect is 0 by construction.
        //
        // For VSD = 90° (east), V-sect = N·cos(90°) + E·sin(90°) = E.
        // So an offset of (North=10, East=200) projects to V-sect 200.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing = 1_000, Easting = 2_000, VerticalReference = 0,
                VerticalSectionDirection = 90,   // project onto east
            });
            db.Surveys.Add(new Survey(wellId, depth: 1000, inclination: 45, azimuth: 90)
            {
                North     = 10,                  // relative-to-tie-on
                East      = 200,
                Northing  = 1_010,               // absolute (tie-on + delta)
                Easting   = 2_200,
                VerticalDepth = 700,
                // Survey.VerticalSection on disk is intentionally NOT
                // set here — the controller must ignore it and
                // compute on the fly from North / East / VSD.
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.Trajectories(jobId, CancellationToken.None);
        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        var well = Assert.Single(rows);

        Assert.Equal(2, well.Points.Count);
        Assert.Equal(0,     well.Points[0].VerticalSection);      // tie-on, by definition
        Assert.Equal(200.0, well.Points[1].VerticalSection, 1e-9); // 10·cos(90°) + 200·sin(90°)
    }

    [Fact]
    public async Task Trajectories_DtoCarriesTypeName()
    {
        // The plot page colors curves by Well type, so the DTO has
        // to carry the SmartEnum's Name (not its int value or the
        // entity reference).
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        await SeedWellAsync(factory, jobId, "T", WellType.Target);
        await SeedWellAsync(factory, jobId, "I", WellType.Injection);
        await SeedWellAsync(factory, jobId, "O", WellType.Offset);

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        Assert.Equal("Injection", rows[0].Type);
        Assert.Equal("Offset",    rows[1].Type);
        Assert.Equal("Target",    rows[2].Type);
    }
}
