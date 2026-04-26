using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Wells;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="MagneticsController"/>.
/// Pins three contracts:
///   * GET 404s when no per-well row exists, 200s with the row
///     when it does.
///   * PUT upserts (creates if missing, updates in place if
///     present) — same payload either way.
///   * DELETE is idempotent (204 even when no row exists) and
///     touches only the per-well row, leaving any legacy per-shot
///     lookup rows alone.
/// </summary>
public class MagneticsControllerTests
{
    private static (MagneticsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new MagneticsController(factory)
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

    private static void AssertProblem(IActionResult result, int expectedStatus)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        Assert.IsType<ProblemDetails>(obj.Value);
    }

    // ---------- GET ----------

    [Fact]
    public async Task Get_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Get(jobId, wellId: 9999, CancellationToken.None);

        AssertProblem(result, 404);
    }

    [Fact]
    public async Task Get_WellExistsButNoMagnetics_ReturnsNotFoundProblem()
    {
        // Distinct 404: well found, no per-well magnetics row. UI
        // treats this as "Not set" rather than a hard error.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Get(jobId, wellId, CancellationToken.None);

        AssertProblem(result, 404);
    }

    [Fact]
    public async Task Get_PerWellRowExists_ReturnsOk()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Magnetics.Add(new Magnetics(bTotal: 50_300, dip: 63, declination: 5)
            {
                WellId = wellId,
            });
            await db.SaveChangesAsync();
        }

        var result = await sut.Get(jobId, wellId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<MagneticsDto>(ok.Value);
        Assert.Equal(wellId, dto.WellId);
        Assert.Equal(50_300, dto.BTotal);
        Assert.Equal(63,     dto.Dip);
        Assert.Equal(5,      dto.Declination);
    }

    [Fact]
    public async Task Get_OnlyLookupRowExists_ReturnsNotFoundProblem()
    {
        // Per-shot lookup rows (WellId IS NULL) must NOT be served
        // through this endpoint — the controller is per-well only.
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Magnetics.Add(new Magnetics(50_000, 60, 5));   // WellId = null
            await db.SaveChangesAsync();
        }

        var result = await sut.Get(jobId, wellId, CancellationToken.None);

        AssertProblem(result, 404);
    }

    // ---------- PUT ----------

    [Fact]
    public async Task Set_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Set(jobId, wellId: 9999,
            new SetMagneticsDto(50_000, 60, 5),
            CancellationToken.None);

        AssertProblem(result, 404);
    }

    [Fact]
    public async Task Set_NoExistingRow_CreatesPerWellRow()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Set(jobId, wellId,
            new SetMagneticsDto(BTotal: 50_300, Dip: 63, Declination: 5),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var stored = await db.Magnetics.SingleAsync(m => m.WellId == wellId);
        Assert.Equal(50_300, stored.BTotal);
        Assert.Equal(63,     stored.Dip);
        Assert.Equal(5,      stored.Declination);
    }

    [Fact]
    public async Task Set_ExistingRow_UpdatesInPlace_DoesNotCreateSecondRow()
    {
        // Upsert path — the second PUT must NOT create a duplicate
        // (the filtered unique index would catch it, but the
        // controller should never even try).
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Magnetics.Add(new Magnetics(50_000, 60, 5) { WellId = wellId });
            await db.SaveChangesAsync();
        }

        var result = await sut.Set(jobId, wellId,
            new SetMagneticsDto(BTotal: 51_000, Dip: 65, Declination: 6),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db2 = factory.NewActiveContext();
        var rows = await db2.Magnetics.Where(m => m.WellId == wellId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(51_000, rows[0].BTotal);
        Assert.Equal(65,     rows[0].Dip);
        Assert.Equal(6,      rows[0].Declination);
    }

    [Fact]
    public async Task Set_DoesNotTouchLookupRows()
    {
        // Cross-pool guard: writing the well's row must not
        // disturb the legacy per-shot lookup rows (WellId IS NULL).
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Magnetics.Add(new Magnetics(50_000, 60, 5));   // lookup row
            db.Magnetics.Add(new Magnetics(57_000, 70, 9));   // another lookup row
            await db.SaveChangesAsync();
        }

        await sut.Set(jobId, wellId,
            new SetMagneticsDto(50_300, 63, 5),
            CancellationToken.None);

        await using var db2 = factory.NewActiveContext();
        var lookupRows = await db2.Magnetics.Where(m => m.WellId == null).ToListAsync();
        Assert.Equal(2, lookupRows.Count);
    }

    // ---------- DELETE ----------

    [Fact]
    public async Task Delete_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        var result = await sut.Delete(jobId, wellId: 9999, CancellationToken.None);

        AssertProblem(result, 404);
    }

    [Fact]
    public async Task Delete_NoExistingRow_IsIdempotent()
    {
        // Idempotent contract: clearing a non-existent reference
        // returns 204 so the UI doesn't have to branch on
        // "was it set?".
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Delete(jobId, wellId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_RemovesPerWellRow_LeavesLookupRowsIntact()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        await using (var db = factory.NewActiveContext())
        {
            db.Magnetics.Add(new Magnetics(50_300, 63, 5) { WellId = wellId });
            db.Magnetics.Add(new Magnetics(50_000, 60, 5));   // lookup row, untouched
            await db.SaveChangesAsync();
        }

        var result = await sut.Delete(jobId, wellId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db2 = factory.NewActiveContext();
        Assert.Empty(await db2.Magnetics.Where(m => m.WellId == wellId).ToListAsync());
        Assert.Single(await db2.Magnetics.Where(m => m.WellId == null).ToListAsync());
    }
}
