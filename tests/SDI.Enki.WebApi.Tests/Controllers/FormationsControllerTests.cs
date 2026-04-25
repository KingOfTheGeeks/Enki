using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells.Formations;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class FormationsControllerTests
{
    private static (FormationsController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new FormationsController(factory)
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

    private static async Task<int> SeedFormationAsync(
        FakeTenantDbContextFactory factory, int wellId,
        string name = "Eagle Ford", double fromV = 5000, double toV = 6000, double res = 8.0)
    {
        await using var db = factory.NewActiveContext();
        var f = new Formation(wellId, name, fromV, toV, res);
        db.Formations.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
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
    public async Task List_ReturnsFormationsOrderedByFromVertical()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        await SeedFormationAsync(factory, wellId, "Austin Chalk", fromV: 4000, toV: 5000, res: 12);
        await SeedFormationAsync(factory, wellId, "Eagle Ford",    fromV: 5000, toV: 6000, res: 8);
        await SeedFormationAsync(factory, wellId, "Buda",          fromV: 3000, toV: 4000, res: 15);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(wellId, CancellationToken.None));
        var rows = ((IEnumerable<FormationSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { "Buda", "Austin Chalk", "Eagle Ford" }, rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var fId = await SeedFormationAsync(factory, wellId, "Eagle Ford");

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(wellId, fId, CancellationToken.None));
        var dto = Assert.IsType<FormationDetailDto>(ok.Value);
        Assert.Equal("Eagle Ford", dto.Name);
    }

    [Fact]
    public async Task Get_FormationUnderDifferentWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellA = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other", WellType.Offset));
            await db.SaveChangesAsync();
        }
        var fId = await SeedFormationAsync(factory, wellA);
        AssertProblem(await sut.Get(wellId: 2, fId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateFormationDto(
                Name: "Eagle Ford",
                FromVertical: 5000, ToVertical: 6000,
                Resistance: 8.0,
                Description: "Source rock"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<FormationSummaryDto>(created.Value);
        Assert.Equal("Eagle Ford", summary.Name);
    }

    [Fact]
    public async Task Create_FromGreaterThanTo_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateFormationDto(
                Name: "Bad",
                FromVertical: 6000, ToVertical: 5000,   // inverted
                Resistance: 8),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        var errors = (IReadOnlyDictionary<string, string[]>)
            ((ProblemDetails)((ObjectResult)result).Value!).Extensions["errors"]!;
        Assert.Contains(nameof(CreateFormationDto.FromVertical), errors.Keys);

        // Atomic — no row persisted on validation failure.
        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Formations.CountAsync());
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        AssertProblem(await sut.Create(99999,
            new CreateFormationDto("X", 0, 100, 1.0),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var fId = await SeedFormationAsync(factory, wellId, "Eagle Ford");

        var result = await sut.Update(wellId, fId,
            new UpdateFormationDto(
                Name: "Eagle Ford Lower",
                FromVertical: 5500, ToVertical: 6200,
                Resistance: 9.5,
                Description: "Lower section"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.Formations.AsNoTracking().FirstAsync(f => f.Id == fId);
        Assert.Equal("Eagle Ford Lower", reloaded.Name);
        Assert.Equal("Lower section", reloaded.Description);
        Assert.Equal(5500d, reloaded.FromVertical);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Update_FromGreaterThanTo_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var fId = await SeedFormationAsync(factory, wellId);

        var result = await sut.Update(wellId, fId,
            new UpdateFormationDto("X", 1000, 500, 1.0, null),
            CancellationToken.None);
        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownFormation_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        AssertProblem(await sut.Update(wellId, 99999,
            new UpdateFormationDto("X", 0, 100, 1.0, null),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var fId = await SeedFormationAsync(factory, wellId);

        Assert.IsType<NoContentResult>(await sut.Delete(wellId, fId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Formations.AnyAsync(f => f.Id == fId));
    }

    [Fact]
    public async Task Delete_UnknownFormation_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        AssertProblem(await sut.Delete(wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
