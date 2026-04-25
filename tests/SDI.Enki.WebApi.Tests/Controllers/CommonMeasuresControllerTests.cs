using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Shared.Wells.CommonMeasures;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

public class CommonMeasuresControllerTests
{
    private static (CommonMeasuresController Controller, FakeTenantDbContextFactory Factory) NewSut()
    {
        var factory = new FakeTenantDbContextFactory();
        var controller = new CommonMeasuresController(factory)
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

    private static async Task<int> SeedMeasureAsync(
        FakeTenantDbContextFactory factory, int wellId,
        double fromV = 0, double toV = 1000, double value = 10)
    {
        await using var db = factory.NewActiveContext();
        var c = new CommonMeasure(wellId, fromV, toV, value);
        db.CommonMeasures.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
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
    public async Task List_ReturnsMeasuresOrderedByFromVertical()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        await SeedMeasureAsync(factory, wellId, fromV: 3000, toV: 4000);
        await SeedMeasureAsync(factory, wellId, fromV: 1000, toV: 2000);
        await SeedMeasureAsync(factory, wellId, fromV: 2000, toV: 3000);

        var ok = Assert.IsType<OkObjectResult>(await sut.List(wellId, CancellationToken.None));
        var rows = ((IEnumerable<CommonMeasureSummaryDto>)ok.Value!).ToList();
        Assert.Equal(new[] { 1000d, 2000d, 3000d }, rows.Select(r => r.FromVertical));
    }

    [Fact]
    public async Task Get_KnownIds_ReturnsDetail()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var mId = await SeedMeasureAsync(factory, wellId, fromV: 100, toV: 200, value: 9.5);

        var ok = Assert.IsType<OkObjectResult>(await sut.Get(wellId, mId, CancellationToken.None));
        var dto = Assert.IsType<CommonMeasureDetailDto>(ok.Value);
        Assert.Equal(9.5d, dto.Value);
    }

    [Fact]
    public async Task Get_MeasureUnderDifferentWell_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellA = await SeedWellAsync(factory);
        await using (var db = factory.NewActiveContext())
        {
            db.Wells.Add(new Well("Other", WellType.Offset));
            await db.SaveChangesAsync();
        }
        var mId = await SeedMeasureAsync(factory, wellA);
        AssertProblem(await sut.Get(wellId: 2, mId, CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidDto_PersistsAndReturnsCreated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateCommonMeasureDto(FromVertical: 1000, ToVertical: 2000, Value: 12.5),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<CommonMeasureSummaryDto>(created.Value);
        Assert.Equal(12.5d, summary.Value);
    }

    [Fact]
    public async Task Create_FromGreaterThanTo_ReturnsValidationProblemAndPersistsNothing()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        var result = await sut.Create(wellId,
            new CreateCommonMeasureDto(FromVertical: 2000, ToVertical: 1000, Value: 1),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");

        await using var db = factory.NewActiveContext();
        Assert.Equal(0, await db.CommonMeasures.CountAsync());
    }

    [Fact]
    public async Task Create_UnknownWell_ReturnsNotFoundProblem()
    {
        var (sut, _) = NewSut();
        AssertProblem(await sut.Create(99999,
            new CreateCommonMeasureDto(0, 100, 1),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Update_ValidDto_RewritesFieldsAndStampsUpdated()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var mId = await SeedMeasureAsync(factory, wellId);

        var result = await sut.Update(wellId, mId,
            new UpdateCommonMeasureDto(FromVertical: 500, ToVertical: 1500, Value: 22),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var db = factory.NewActiveContext();
        var reloaded = await db.CommonMeasures.AsNoTracking().FirstAsync(c => c.Id == mId);
        Assert.Equal(500d, reloaded.FromVertical);
        Assert.Equal(1500d, reloaded.ToVertical);
        Assert.Equal(22d, reloaded.Value);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Update_FromGreaterThanTo_ReturnsValidationProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var mId = await SeedMeasureAsync(factory, wellId);

        var result = await sut.Update(wellId, mId,
            new UpdateCommonMeasureDto(2000, 1000, 1),
            CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Update_UnknownMeasure_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);

        AssertProblem(await sut.Update(wellId, 99999,
            new UpdateCommonMeasureDto(0, 100, 1),
            CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task Delete_KnownIds_RemovesRow()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        var mId = await SeedMeasureAsync(factory, wellId);

        Assert.IsType<NoContentResult>(await sut.Delete(wellId, mId, CancellationToken.None));

        await using var db = factory.NewActiveContext();
        Assert.False(await db.CommonMeasures.AnyAsync(c => c.Id == mId));
    }

    [Fact]
    public async Task Delete_UnknownMeasure_ReturnsNotFoundProblem()
    {
        var (sut, factory) = NewSut();
        var wellId = await SeedWellAsync(factory);
        AssertProblem(await sut.Delete(wellId, 99999, CancellationToken.None), 404, "/not-found");
    }
}
