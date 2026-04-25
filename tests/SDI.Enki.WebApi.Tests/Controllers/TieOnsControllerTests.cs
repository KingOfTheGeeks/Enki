using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells.TieOns;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="TieOnsController"/>. Mirrors
/// the shape of <see cref="WellsControllerTests"/>; each test gets a
/// fresh InMemory tenant context.
/// </summary>
public class TieOnsControllerTests
{
    // ---------- fixture helpers ----------

    private static (TieOnsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new TieOnsController(factory)
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
        return (controller, factory);
    }

    private static async Task<int> SeedWellAsync(FakeTenantDbContextFactory factory)
    {
        await using var db = factory.NewActiveContext();
        var well = new Well("Johnson 1H", WellType.Target);
        db.Wells.Add(well);
        await db.SaveChangesAsync();
        return well.Id;
    }

    private static async Task<int> SeedTieOnAsync(
        FakeTenantDbContextFactory factory,
        int wellId,
        double depth = 1000)
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

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        var result = await sut.List(99999, CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task List_KnownWellNoTieOns_ReturnsEmpty()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.List(wellId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<IEnumerable<TieOnSummaryDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task List_ReturnsTieOnsOrderedByDepth()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        // Insert out of order to prove the ORDER BY actually fires.
        await SeedTieOnAsync(factory, wellId, depth: 3000);
        await SeedTieOnAsync(factory, wellId, depth: 1000);
        await SeedTieOnAsync(factory, wellId, depth: 2000);

        var result = await sut.List(wellId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = ((IEnumerable<TieOnSummaryDto>)ok.Value!).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.Depth));
    }

    [Fact]
    public async Task List_ScopesToTheGivenWellOnly()
    {
        var (sut, factory) = NewSut();
        var well1 = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other-1H", WellType.Target));
            await db.SaveChangesAsync();
        }
        var well2 = 2;   // FakeTenantDbContextFactory's InMemory provider auto-increments.

        await SeedTieOnAsync(factory, well1, depth: 1000);
        await SeedTieOnAsync(factory, well2, depth: 2000);

        var result = await sut.List(well1, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = ((IEnumerable<TieOnSummaryDto>)ok.Value!).ToList();
        var only = Assert.Single(rows);
        Assert.Equal(1000d, only.Depth);
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var wellId  = await SeedWellAsync(factory);
        var tieOnId = await SeedTieOnAsync(factory, wellId, depth: 1500);

        var result = await sut.Get(wellId, tieOnId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TieOnDetailDto>(ok.Value);
        Assert.Equal(tieOnId, dto.Id);
        Assert.Equal(wellId, dto.WellId);
        Assert.Equal(1500d, dto.Depth);
    }

    [Fact]
    public async Task Get_UnknownTieOn_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Get(wellId, 99999, CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Get_TieOnUnderDifferentWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellA = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other", WellType.Offset));
            await db.SaveChangesAsync();
        }
        // Tie-on belongs to wellA but we ask for it under wellB → 404.
        var tieOnId = await SeedTieOnAsync(factory, wellA);
        var wellB   = 2;

        var result = await sut.Get(wellB, tieOnId, CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Create
    // ============================================================

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateTieOnDto(
                Depth:                    1234,
                Inclination:              12,
                Azimuth:                  180,
                North:                    100,
                East:                     200,
                Northing:                 1100,
                Easting:                  2200,
                VerticalReference:        1230,
                SubSeaReference:          50,
                VerticalSectionDirection: 90),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<TieOnSummaryDto>(created.Value);
        Assert.Equal(1234d, summary.Depth);

        await using var db = factory.NewActiveContext();
        var stored = await db.TieOns.AsNoTracking().FirstAsync();
        Assert.Equal(180d, stored.Azimuth);
        Assert.Equal(2200d, stored.Easting);
        Assert.Equal("system", stored.CreatedBy);
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        var result = await sut.Create(99999,
            new CreateTieOnDto(Depth: 0, Inclination: 0, Azimuth: 0),
            CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var wellId  = await SeedWellAsync(factory);
        var tieOnId = await SeedTieOnAsync(factory, wellId);

        var result = await sut.Update(wellId, tieOnId,
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
        Assert.Equal(30d, reloaded.Inclination);
        Assert.Equal(270d, reloaded.Azimuth);
        Assert.Equal(7d, reloaded.VerticalSectionDirection);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Update_UnknownTieOn_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Update(wellId, 99999,
            new UpdateTieOnDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Update_TieOnUnderDifferentWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellA = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other", WellType.Offset));
            await db.SaveChangesAsync();
        }
        var tieOnId = await SeedTieOnAsync(factory, wellA);

        var result = await sut.Update(wellId: 2, tieOnId,
            new UpdateTieOnDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Delete
    // ============================================================

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var wellId  = await SeedWellAsync(factory);
        var tieOnId = await SeedTieOnAsync(factory, wellId);

        var result = await sut.Delete(wellId, tieOnId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await using var db = factory.NewActiveContext();
        Assert.False(await db.TieOns.AnyAsync(t => t.Id == tieOnId));
    }

    [Fact]
    public async Task Delete_UnknownTieOn_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Delete(wellId, 99999, CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }
}
