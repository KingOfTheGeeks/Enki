using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Concurrency;
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
        var well = new Well(jobId, name, type ?? WellType.Target)
        {
            RowVersion = TestRowVersionBytes,
        };
        db.Wells.Add(well);
        await db.SaveChangesAsync();
        return well.Id;
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
    public async Task Create_ValidDto_AutoCreatesZeroTieOn()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Create(jobId,
            new CreateWellDto(Name: "Auto-tie", Type: "Target"),
            CancellationToken.None);

        var summary = Assert.IsType<WellSummaryDto>(
            Assert.IsType<CreatedAtActionResult>(result).Value);

        // Every Well auto-gets exactly one zero-valued TieOn so
        // Marduk's minimum-curvature calc has an anchor on the very
        // first survey-add. The user can edit the values via TieOn
        // edit; "Delete tie-on" zeros the row back out instead of
        // removing it. Without this, RecalculateAsync silently no-ops
        // and the first survey lands with zero computed columns.
        await using var db = factory.NewActiveContext();
        var tieOn = await db.TieOns.AsNoTracking().SingleAsync(t => t.WellId == summary.Id);
        Assert.Equal(0d, tieOn.Depth);
        Assert.Equal(0d, tieOn.Inclination);
        Assert.Equal(0d, tieOn.Azimuth);
        Assert.Equal(0d, tieOn.North);
        Assert.Equal(0d, tieOn.East);
        Assert.Equal(0d, tieOn.Northing);
        Assert.Equal(0d, tieOn.Easting);
        Assert.Equal(0d, tieOn.VerticalReference);
        Assert.Equal(0d, tieOn.SubSeaReference);
        Assert.Equal(0d, tieOn.VerticalSectionDirection);
        Assert.Equal(1, summary.TieOnCount);
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
            new UpdateWellDto(Name: "New", Type: "Offset", RowVersion: TestRowVersion),
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
            new UpdateWellDto("x", "Target", RowVersion: TestRowVersion),
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
            new UpdateWellDto("x", "Target", RowVersion: TestRowVersion),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId,
            new UpdateWellDto("x", "NotAWellType", RowVersion: TestRowVersion),
            CancellationToken.None), 400, "/validation");
    }

    // ============================================================
    // Delete
    // ============================================================

    [Fact]
    public async Task Delete_NoChildren_SoftArchivesRow()
    {
        // Post-soft-delete: the row stays in the DB with ArchivedAt
        // set; the global query filter hides it from default reads
        // but IgnoreQueryFilters() still finds it. Restorable.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Wells.AnyAsync(w => w.Id == wellId)); // hidden by filter
        var archived = await db.Wells.IgnoreQueryFilters()
            .SingleAsync(w => w.Id == wellId);
        Assert.NotNull(archived.ArchivedAt);
    }

    [Fact]
    public async Task Delete_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.Delete(jobId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_WithChildSurvey_NowSoftArchivesInsteadOf409()
    {
        // Soft-delete is non-destructive — child rows aren't at risk
        // of being lost, so the previous "Has children → 409" guard
        // is gone. The well is archived; child rows remain in the DB
        // and remain reachable via IgnoreQueryFilters() if needed.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Surveys.Add(new Survey(wellId, depth: 1000, inclination: 0, azimuth: 0));
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, CancellationToken.None));

        await using var db2 = factory.NewActiveContext();
        var archived = await db2.Wells.IgnoreQueryFilters()
            .SingleAsync(w => w.Id == wellId);
        Assert.NotNull(archived.ArchivedAt);
        // Survey row is preserved verbatim — soft-delete doesn't
        // cascade. A future "purge archived wells" cleanup job
        // would handle deep removal.
        Assert.True(await db2.Surveys.AnyAsync(s => s.WellId == wellId));
    }

    [Fact]
    public async Task Delete_AlreadyArchived_Returns404()
    {
        // Once a well is archived the global query filter hides it —
        // re-deleting via the same URL surfaces as "Well doesn't exist"
        // (404) just like a never-existed well. That's the cleanest
        // semantic for a stale browser tab; admins who explicitly want
        // to re-archive (no meaningful operation) can hit Restore first
        // or use IgnoreQueryFilters() server-side.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, CancellationToken.None));

        AssertProblem(
            await sut.Delete(jobId, wellId, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Restore_ArchivedWell_ClearsArchivedAt()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await sut.Delete(jobId, wellId, CancellationToken.None);

        Assert.IsType<NoContentResult>(
            await sut.Restore(jobId, wellId, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));

        await using var db = factory.NewActiveContext();
        var w = await db.Wells.SingleAsync(x => x.Id == wellId); // visible again
        Assert.Null(w.ArchivedAt);
    }

    [Fact]
    public async Task Restore_AlreadyActiveWell_IsIdempotent()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        // Without prior delete — well is already active.
        Assert.IsType<NoContentResult>(
            await sut.Restore(jobId, wellId, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));
    }

    [Fact]
    public async Task Restore_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.Restore(jobId, 99999, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task ListArchived_ReturnsOnlyArchivedWellsForJob()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);

        var liveId   = await SeedWellAsync(factory, jobId, name: "Active");
        var archivedId = await SeedWellAsync(factory, jobId, name: "Archived");

        await sut.Delete(jobId, archivedId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.ListArchived(jobId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<WellSummaryDto>>(ok.Value).ToList();

        Assert.Single(rows);
        Assert.Equal(archivedId, rows[0].Id);
        Assert.DoesNotContain(rows, w => w.Id == liveId);
    }

    [Fact]
    public async Task List_ExcludesArchivedWells()
    {
        // The default List endpoint goes through the global query
        // filter — archived wells must not appear.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var liveId   = await SeedWellAsync(factory, jobId, name: "Live");
        var deadId = await SeedWellAsync(factory, jobId, name: "Dead");

        await sut.Delete(jobId, deadId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.List(jobId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<WellSummaryDto>>(ok.Value).ToList();

        Assert.Single(rows);
        Assert.Equal(liveId, rows[0].Id);
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
        await SeedWellAsync(factory, jobId, "Mike 14I",     WellType.Intercept);

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        Assert.Equal(
            new[] { "Adam 1", "Mike 14I", "Zara 9H" },
            rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Trajectories_VerticalSection_RelaysCachedSurveyValue_TieOnIsZero()
    {
        // V-sect is now populated correctly by Marduk's MinimumCurvature
        // (relative-to-tie-on) and the controller relays the cached
        // value straight through. Tie-on still gets 0 because the
        // TieOn entity doesn't have a VerticalSection field — it's
        // the origin of the projection.
        //
        // This test pins the contract: whatever Survey.VerticalSection
        // says, the API echoes; tie-on is always 0.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
            {
                Northing = 1_000, Easting = 2_000, VerticalReference = 0,
                VerticalSectionDirection = 90,
            });
            db.Surveys.Add(new Survey(wellId, depth: 1000, inclination: 45, azimuth: 90)
            {
                Northing = 1_010, Easting = 2_200, VerticalDepth = 700,
                VerticalSection = 200.0,    // pre-cached value the API should relay
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.Trajectories(jobId, CancellationToken.None);
        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        var well = Assert.Single(rows);

        Assert.Equal(2, well.Points.Count);
        Assert.Equal(0,     well.Points[0].VerticalSection);      // tie-on, by definition
        Assert.Equal(200.0, well.Points[1].VerticalSection, 1e-9); // verbatim relay of cached value
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
        await SeedWellAsync(factory, jobId, "I", WellType.Intercept);
        await SeedWellAsync(factory, jobId, "O", WellType.Offset);

        var result = await sut.Trajectories(jobId, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IEnumerable<WellTrajectoryDto>>(
            ((OkObjectResult)result).Value!).ToList();
        Assert.Equal("Intercept", rows[0].Type);
        Assert.Equal("Offset",    rows[1].Type);
        Assert.Equal("Target",    rows[2].Type);
    }

    // =====================================================================
    // GET /tenants/{code}/jobs/{jobId}/wells/{wellId}/anti-collision
    //
    // Travelling-cylinder anti-collision scan: target well + every
    // sibling under the same Job. Math owner is Marduk's
    // AntiCollisionScanner; the controller's job is the
    // load-and-rehydrate-SurveyStation plumbing plus the
    // self-exclude / cross-job-exclude / empty-trajectory-skip
    // filtering.
    //
    // Helper notes: Tie-ons + Surveys here carry hand-set
    // Northing / Easting / VerticalDepth — the in-memory Fake DB
    // doesn't run Marduk's recalc, and the scanner reads N/E/TVD
    // straight off the stations. Setting them by hand mirrors what
    // the recalc would persist for the test geometries below.
    // =====================================================================

    /// <summary>
    /// Seed a well with a tie-on at (north, east, tvd=0) and a
    /// single survey at (north, east, tvd) — i.e. a perfectly
    /// vertical well dropped straight down at the given grid
    /// coordinates. Used by the anti-collision tests so the
    /// expected closest-approach distances are trivial to compute
    /// by hand (just horizontal Pythagorean separation between
    /// well grid coords).
    /// </summary>
    private static async Task SeedVerticalWellAsync(
        FakeTenantDbContextFactory factory,
        int wellId,
        double northing,
        double easting,
        double tvd)
    {
        await using var db = factory.NewActiveContext();
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing = northing, Easting = easting, VerticalReference = 0,
        });
        db.Surveys.Add(new Survey(wellId, depth: tvd, inclination: 0, azimuth: 0)
        {
            Northing = northing, Easting = easting, VerticalDepth = tvd,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AntiCollision_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(
            await sut.AntiCollision(jobId, 99999, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task AntiCollision_WellUnderDifferentJob_ReturnsNotFoundProblem()
    {
        // Cross-job leak guard — same shape as Get, Update, Delete.
        var (sut, factory) = NewSut();
        var jobA   = await SeedJobAsync(factory);
        var jobB   = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);

        AssertProblem(
            await sut.AntiCollision(jobB, wellId, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task AntiCollision_TargetHasNoStations_ReturnsEmptyList()
    {
        // Well exists but no tie-on + no surveys: nothing to scan
        // FROM. Empty list, not 404 — the well exists, the user just
        // hasn't loaded any data on it yet.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, wellId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task AntiCollision_NoOffsetWells_ReturnsEmptyList()
    {
        // Job has only the target — no siblings to scan against.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId, "Lone Star 14H");
        await SeedVerticalWellAsync(factory, wellId, 0, 0, 1000);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, wellId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task AntiCollision_AllOffsetsEmpty_ReturnsEmptyList()
    {
        // Siblings exist but none have any trajectory data — drop
        // them silently rather than emit empty scan rows.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var target = await SeedWellAsync(factory, jobId, "Lone Star 14H");
        await SeedVerticalWellAsync(factory, target, 0, 0, 1000);
        await SeedWellAsync(factory, jobId, "Empty Sibling A");
        await SeedWellAsync(factory, jobId, "Empty Sibling B");

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, target, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task AntiCollision_HappyPath_ReturnsOneScanPerNonEmptyOffset()
    {
        // Target at (0, 0); offsets at (0, 100) and (0, 200) — wells
        // are vertical so the closest-approach distance to each
        // offset is a flat horizontal separation (100 and 200).
        var (sut, factory) = NewSut();
        var jobId    = await SeedJobAsync(factory);
        var target   = await SeedWellAsync(factory, jobId, "Lone Star 14H", WellType.Target);
        var offsetA  = await SeedWellAsync(factory, jobId, "Lambert 2I",    WellType.Intercept);
        var offsetB  = await SeedWellAsync(factory, jobId, "Pearson 1",     WellType.Offset);

        await SeedVerticalWellAsync(factory, target,  northing: 0, easting:   0, tvd: 1000);
        await SeedVerticalWellAsync(factory, offsetA, northing: 0, easting: 100, tvd: 1000);
        await SeedVerticalWellAsync(factory, offsetB, northing: 0, easting: 200, tvd: 1000);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, target, CancellationToken.None));
        var scans = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value).ToList();

        Assert.Equal(2, scans.Count);
        // Alphabetical by name → Lambert 2I before Pearson 1.
        Assert.Equal("Lambert 2I", scans[0].OffsetWellName);
        Assert.Equal("Pearson 1",  scans[1].OffsetWellName);

        // Each target station gets a sample row; target has tie-on +
        // 1 survey = 2 stations, so 2 samples per offset.
        Assert.Equal(2, scans[0].Samples.Count);
        Assert.Equal(2, scans[1].Samples.Count);

        // Vertical wells separated by horizontal distance only —
        // closest approach is exactly the grid separation.
        Assert.All(scans[0].Samples, s => Assert.Equal(100, s.Distance, 1e-6));
        Assert.All(scans[1].Samples, s => Assert.Equal(200, s.Distance, 1e-6));
    }

    [Fact]
    public async Task AntiCollision_ExcludesTargetItself()
    {
        // Self-comparison is meaningless (distance is always 0) and
        // would dominate the chart's lower bound. Confirm the target
        // never appears as its own offset.
        var (sut, factory) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var target  = await SeedWellAsync(factory, jobId, "Lone Star 14H", WellType.Target);
        var offset  = await SeedWellAsync(factory, jobId, "Lambert 2I",    WellType.Intercept);

        await SeedVerticalWellAsync(factory, target, 0, 0,   1000);
        await SeedVerticalWellAsync(factory, offset, 0, 100, 1000);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, target, CancellationToken.None));
        var scans = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value).ToList();

        var single = Assert.Single(scans);
        Assert.Equal("Lambert 2I", single.OffsetWellName);
        Assert.NotEqual(target, single.OffsetWellId);
    }

    [Fact]
    public async Task AntiCollision_ExcludesWellsUnderOtherJobs()
    {
        // A well belonging to a different Job under the same tenant
        // must not bleed into the offset list — anti-collision is
        // strictly job-scoped.
        var (sut, factory) = NewSut();
        var jobA = await SeedJobAsync(factory);
        var jobB = await SeedJobAsync(factory);

        var target  = await SeedWellAsync(factory, jobA, "Target A");
        var sibling = await SeedWellAsync(factory, jobA, "Sibling A");
        var stranger = await SeedWellAsync(factory, jobB, "Stranger From Other Job");

        await SeedVerticalWellAsync(factory, target,   0, 0,   1000);
        await SeedVerticalWellAsync(factory, sibling,  0, 100, 1000);
        await SeedVerticalWellAsync(factory, stranger, 0, 50,  1000);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobA, target, CancellationToken.None));
        var scans = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value).ToList();

        var single = Assert.Single(scans);
        Assert.Equal("Sibling A", single.OffsetWellName);
        Assert.DoesNotContain(scans, s => s.OffsetWellName == "Stranger From Other Job");
    }

    [Fact]
    public async Task AntiCollision_DtoCarriesOffsetIdAndType()
    {
        // The rendering side wires hover-clicks back through to the
        // offset's well-detail page (uses OffsetWellId) and colours
        // curves by type the same way the trajectories plot does.
        var (sut, factory) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var target  = await SeedWellAsync(factory, jobId, "T", WellType.Target);
        var offset  = await SeedWellAsync(factory, jobId, "Lambert 2I", WellType.Intercept);

        await SeedVerticalWellAsync(factory, target, 0, 0,   1000);
        await SeedVerticalWellAsync(factory, offset, 0, 100, 1000);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, target, CancellationToken.None));
        var scans = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value).ToList();

        var single = Assert.Single(scans);
        Assert.Equal(offset,       single.OffsetWellId);
        Assert.Equal("Lambert 2I", single.OffsetWellName);
        Assert.Equal("Intercept",  single.OffsetWellType);
    }

    [Fact]
    public async Task AntiCollision_SamplesPreserveTargetMdAndTvd()
    {
        // Each sample row corresponds to one target station; MD + TVD
        // pass through unchanged from the persisted Survey / TieOn
        // values so the chart can plot distance vs depth without
        // having to re-fetch the trajectory separately.
        var (sut, factory) = NewSut();
        var jobId   = await SeedJobAsync(factory);
        var target  = await SeedWellAsync(factory, jobId, "T", WellType.Target);
        var offset  = await SeedWellAsync(factory, jobId, "O", WellType.Offset);

        await SeedVerticalWellAsync(factory, target, 0, 0,   1500);
        await SeedVerticalWellAsync(factory, offset, 0, 100, 1500);

        var ok = Assert.IsType<OkObjectResult>(
            await sut.AntiCollision(jobId, target, CancellationToken.None));
        var scan = Assert.IsAssignableFrom<IEnumerable<AntiCollisionScanDto>>(ok.Value).Single();

        // Tie-on (depth 0, tvd 0) + survey (depth 1500, tvd 1500).
        Assert.Equal(2, scan.Samples.Count);
        Assert.Equal(0,    scan.Samples[0].TargetMd);
        Assert.Equal(0,    scan.Samples[0].TargetTvd);
        Assert.Equal(1500, scan.Samples[1].TargetMd);
        Assert.Equal(1500, scan.Samples[1].TargetTvd);
    }
}
