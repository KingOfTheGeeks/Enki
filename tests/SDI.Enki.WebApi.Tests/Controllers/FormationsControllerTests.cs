using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
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
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(await sut.List(jobId, 99999, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task List_ReturnsFormationsOrderedByFromVertical()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        await SeedFormationAsync(factory, wellId, "Austin Chalk", 4000, 5000, 12);
        await SeedFormationAsync(factory, wellId, "Eagle Ford",   5000, 6000, 8);
        await SeedFormationAsync(factory, wellId, "Buda",         3000, 4000, 15);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(jobId, wellId, CancellationToken.None));
        var rows = ((IEnumerable<FormationSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { "Buda", "Austin Chalk", "Eagle Ford" }, rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var fId    = await SeedFormationAsync(factory, wellId, "Eagle Ford");

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(jobId, wellId, fId, CancellationToken.None));
        var dto = Assert.IsType<FormationDetailDto>(ok.Value);
        Assert.Equal("Eagle Ford", dto.Name);
    }

    [Fact]
    public async Task Get_FormationUnderDifferentJob_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobA   = await SeedJobAsync(factory);
        var jobB   = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobA);
        var fId    = await SeedFormationAsync(factory, wellId);

        AssertProblem(await sut.Get(jobB, wellId, fId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
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
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        var result = await sut.Create(jobId, wellId,
            new CreateFormationDto("Bad", 6000, 5000, 8),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.Formations.CountAsync());
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(await sut.Create(jobId, 99999,
            new CreateFormationDto("X", 0, 100, 1.0),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var fId    = await SeedFormationAsync(factory, wellId, "Eagle Ford");

        var result = await sut.Update(jobId, wellId, fId,
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
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Update_FromGreaterThanTo_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var fId    = await SeedFormationAsync(factory, wellId);

        AssertProblem(await sut.Update(jobId, wellId, fId,
            new UpdateFormationDto("X", 1000, 500, 1.0, null),
            CancellationToken.None), 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownFormation_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Update(jobId, wellId, 99999,
            new UpdateFormationDto("X", 0, 100, 1.0, null),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);
        var fId    = await SeedFormationAsync(factory, wellId);

        Assert.IsType<NoContentResult>(
            await sut.Delete(jobId, wellId, fId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.Formations.AnyAsync(f => f.Id == fId));
    }

    [Fact]
    public async Task Delete_UnknownFormation_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var jobId  = await SeedJobAsync(factory);
        var wellId = await SeedWellAsync(factory, jobId);

        AssertProblem(await sut.Delete(jobId, wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
