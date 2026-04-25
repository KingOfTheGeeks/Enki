using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="WellsController"/>. Uses a
/// per-test InMemory <see cref="SDI.Enki.Infrastructure.Data.TenantDbContext"/>
/// via <see cref="FakeTenantDbContextFactory"/> so each test gets isolated
/// state. Mirrors the shape of <see cref="TenantsControllerTests"/>.
/// </summary>
public class WellsControllerTests
{
    // ---------- fixture helpers ----------

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
                    // CreatedAtAction needs tenantCode in route values to
                    // build the Location header; provide one so Create()
                    // doesn't NRE.
                    Values = { ["tenantCode"] = "TENANTTEST" },
                },
            },
        };
        return (controller, factory);
    }

    private static async Task<int> SeedWellAsync(
        FakeTenantDbContextFactory factory,
        string name = "Johnson 1H",
        WellType? type = null)
    {
        await using var db = factory.NewActiveContext();
        var well = new Well(name, type ?? WellType.Target);
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
    public async Task List_NoWells_ReturnsEmpty()
    {
        var (sut, _) = NewSut();
        var result = await sut.List(CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task List_WithWells_ReturnsSummariesOrderedByName()
    {
        var (sut, factory) = NewSut();
        await SeedWellAsync(factory, "Zebra-1H");
        await SeedWellAsync(factory, "Alpha-1H");
        await SeedWellAsync(factory, "Mike-1H");

        var rows = (await sut.List(CancellationToken.None)).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Alpha-1H", "Mike-1H", "Zebra-1H" }, rows.Select(w => w.Name));
        Assert.All(rows, r => Assert.Equal(0, r.SurveyCount));
        Assert.All(rows, r => Assert.Equal(0, r.TieOnCount));
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_KnownId_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var id = await SeedWellAsync(factory, "Johnson 1H", WellType.Target);

        var result = await sut.Get(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<WellDetailDto>(ok.Value);
        Assert.Equal(id, dto.Id);
        Assert.Equal("Johnson 1H", dto.Name);
        Assert.Equal("Target", dto.Type);
        Assert.Equal(0, dto.SurveyCount);
        Assert.Equal(0, dto.TieOnCount);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        var result = await sut.Get(99999, CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Create
    // ============================================================

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();

        var result = await sut.Create(
            new CreateWellDto(Name: "New Well", Type: "Target"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(WellsController.Get), created.ActionName);
        var summary = Assert.IsType<WellSummaryDto>(created.Value);
        Assert.Equal("New Well", summary.Name);
        Assert.Equal("Target", summary.Type);

        await using var db = factory.NewActiveContext();
        var stored = await db.Wells.AsNoTracking().FirstAsync();
        Assert.Equal("New Well", stored.Name);
        Assert.Equal("system", stored.CreatedBy);
        Assert.True(stored.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Create_UnknownType_ReturnsValidationProblem()
    {
        var (sut, _) = NewSut();

        var result = await sut.Create(
            new CreateWellDto(Name: "Bad Type Well", Type: "Bogus"),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        // EnkiResults.ValidationProblem stashes the field-error dictionary
        // under Extensions["errors"] (not the typed ValidationProblemDetails
        // shape) so the wire format matches NotFound/Conflict/etc.
        var problem = (ProblemDetails)((ObjectResult)result).Value!;
        var errors  = (IReadOnlyDictionary<string, string[]>)problem.Extensions["errors"]!;
        Assert.Contains(nameof(CreateWellDto.Type), errors.Keys);
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_ValidDto_RenamesAndStampsUpdatedAudit()
    {
        var (sut, factory) = NewSut();
        var id = await SeedWellAsync(factory, "Old Name", WellType.Target);

        var result = await sut.Update(id,
            new UpdateWellDto(Name: "New Name", Type: "Offset"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Wells.AsNoTracking().FirstAsync(w => w.Id == id);
        Assert.Equal("New Name", reloaded.Name);
        Assert.Equal(WellType.Offset, reloaded.Type);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        var result = await sut.Update(99999,
            new UpdateWellDto(Name: "x", Type: "Target"),
            CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Update_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var id = await SeedWellAsync(factory);

        var result = await sut.Update(id,
            new UpdateWellDto(Name: "x", Type: "NotAWellType"),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    // ============================================================
    // Delete
    // ============================================================

    [Fact]
    public async Task Delete_NoChildren_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var id = await SeedWellAsync(factory);

        var result = await sut.Delete(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await using var db = factory.NewActiveContext();
        Assert.False(await db.Wells.AnyAsync(w => w.Id == id));
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        var result = await sut.Delete(99999, CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Delete_WithChildSurvey_ReturnsConflictProblem()
    {
        var (sut, factory) = NewSut();
        var id = await SeedWellAsync(factory);

        await using (var db = factory.NewActiveContext())
        {
            db.Surveys.Add(new Survey(id, depth: 1000, inclination: 0, azimuth: 0));
            await db.SaveChangesAsync();
        }

        var result = await sut.Delete(id, CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
        // Well must still be present.
        await using var db2 = factory.NewActiveContext();
        Assert.True(await db2.Wells.AnyAsync(w => w.Id == id));
    }

    [Fact]
    public async Task Delete_WithChildTieOn_ReturnsConflictProblem()
    {
        var (sut, factory) = NewSut();
        var id = await SeedWellAsync(factory);

        await using (var db = factory.NewActiveContext())
        {
            db.TieOns.Add(new TieOn(id, depth: 0, inclination: 0, azimuth: 0));
            await db.SaveChangesAsync();
        }

        var result = await sut.Delete(id, CancellationToken.None);
        AssertProblem(result, 409, "/conflict");
    }
}
