using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells.Tubulars;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class TubularsControllerTests
{
    private static (TubularsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new TubularsController(factory)
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

    private static async Task<int> SeedTubularAsync(
        FakeTenantDbContextFactory factory, int wellId, int order, TubularType? type = null)
    {
        await using var db = factory.NewActiveContext();
        var t = new Tubular(wellId, order, type ?? TubularType.Casing,
            fromMeasured: 0, toMeasured: 1000, diameter: 9.625, weight: 47);
        db.Tubulars.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
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
        var (sut, _) = NewSut();
        AssertProblem(await sut.List(99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task List_ReturnsTubularsOrderedByOrder()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        await SeedTubularAsync(factory, wellId, order: 2);
        await SeedTubularAsync(factory, wellId, order: 0);
        await SeedTubularAsync(factory, wellId, order: 1);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(wellId, CancellationToken.None));
        var rows = ((IEnumerable<TubularSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(r => r.Order));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var tubularId = await SeedTubularAsync(factory, wellId, order: 0, type: TubularType.Liner);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(wellId, tubularId, CancellationToken.None));
        var dto = Assert.IsType<TubularDetailDto>(ok.Value);
        Assert.Equal("Liner", dto.Type);
    }

    [Fact]
    public async Task Get_TubularUnderDifferentWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellA = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other", WellType.Offset));
            await db.SaveChangesAsync();
        }
        var tubularId = await SeedTubularAsync(factory, wellA, 0);
        AssertProblem(await sut.Get(wellId: 2, tubularId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateTubularDto(
                Type: "Casing", Order: 0,
                FromMeasured: 0, ToMeasured: 5000,
                Diameter: 13.375, Weight: 68,
                Name: "Surface casing"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<TubularSummaryDto>(created.Value);
        Assert.Equal("Casing", summary.Type);
        Assert.Equal("Surface casing", summary.Name);

        await using var db = factory.NewActiveContext();
        var stored = await db.Tubulars.AsNoTracking().FirstAsync();
        Assert.Equal("system", stored.CreatedBy);
    }

    [Fact]
    public async Task Create_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateTubularDto("Bogus", 0, 0, 100, 9.625, 47),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        var errors = (IReadOnlyDictionary<string, string[]>)
            ((ProblemDetails)((ObjectResult)result).Value!).Extensions["errors"]!;
        Assert.Contains(nameof(CreateTubularDto.Type), errors.Keys);
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        var result = await sut.Create(99999,
            new CreateTubularDto("Casing", 0, 0, 100, 9.625, 47),
            CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var tubularId = await SeedTubularAsync(factory, wellId, 0, TubularType.Casing);

        var result = await sut.Update(wellId, tubularId,
            new UpdateTubularDto(
                Type: "DrillPipe", Order: 5,
                FromMeasured: 100, ToMeasured: 9000,
                Diameter: 5.0, Weight: 19.5,
                Name: "DP string"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Tubulars.AsNoTracking().FirstAsync(t => t.Id == tubularId);
        Assert.Equal(TubularType.DrillPipe, reloaded.Type);
        Assert.Equal(5, reloaded.Order);
        Assert.Equal("DP string", reloaded.Name);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Update_UnknownType_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var tubularId = await SeedTubularAsync(factory, wellId, 0);

        var result = await sut.Update(wellId, tubularId,
            new UpdateTubularDto("NotAType", 0, 0, 100, 9.625, 47, null),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownTubular_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Update(wellId, 99999,
            new UpdateTubularDto("Casing", 0, 0, 100, 9.625, 47, null),
            CancellationToken.None);
        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var tubularId = await SeedTubularAsync(factory, wellId, 0);

        Assert.IsType<NoContentResult>(await sut.Delete(wellId, tubularId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Tubulars.AnyAsync(t => t.Id == tubularId));
    }

    [Fact]
    public async Task Delete_UnknownTubular_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        AssertProblem(await sut.Delete(wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
