using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Logs;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Logs;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="LogsController"/>. Same
/// shape as <c>RunsControllerTests</c> + <c>SurveysControllerTests</c>:
/// fake tenant-DB factory; bypass auth + tenant routing; cover CRUD
/// + parent-pair guards + concurrency.
/// </summary>
public class LogsControllerTests
{
    private const string TestTenantCode = "ACME";

    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    private static LogsController NewController(FakeTenantDbContextFactory factory)
    {
        var controller = new LogsController(factory);
        var routeData = new RouteData();
        routeData.Values["tenantCode"] = TestTenantCode;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
            RouteData = routeData,
        };
        return controller;
    }

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedTypeSuffix)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.EndsWith(expectedTypeSuffix, problem.Type);
    }

    private static async Task<(Guid jobId, Guid runId)> SeedJobAndRunAsync(
        FakeTenantDbContextFactory factory,
        RunStatus? status = null)
    {
        await using var db = factory.NewActiveContext();
        var job = new Job("Job-A", "for runs", UnitSystem.Field) { RowVersion = TestRowVersionBytes };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        // Seeded run carries a ToolId so LogsController.Create's
        // tool-required gate passes. Magnetics required at the FK level.
        var run = new Run("Run-A", "the run", 0, 100, RunType.Gradient)
        {
            JobId      = job.Id,
            Status     = status ?? RunStatus.Active,
            ToolId     = Guid.NewGuid(),
            Magnetics  = new Magnetics(50000, 60, 0),
            RowVersion = TestRowVersionBytes,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return (job.Id, run.Id);
    }

    private static async Task<int> SeedLogAsync(
        FakeTenantDbContextFactory factory,
        Guid runId,
        string shotName = "shot-1",
        DateTimeOffset? fileTime = null)
    {
        await using var db = factory.NewActiveContext();
        var log = new Log(runId, shotName, fileTime ?? DateTimeOffset.UtcNow)
        {
            RowVersion = TestRowVersionBytes,
        };
        db.Logs.Add(log);
        await db.SaveChangesAsync();
        return log.Id;
    }

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_UnknownRun_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, _) = await SeedJobAndRunAsync(factory);
        AssertProblem(
            await NewController(factory).List(jobId, Guid.NewGuid(), CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task List_WrongJobForRun_ReturnsNotFoundProblem()
    {
        // Parent-pair guard: jobId + runId must match. A made-up jobId
        // for a real runId still 404s.
        var factory = new FakeTenantDbContextFactory();
        var (_, runId) = await SeedJobAndRunAsync(factory);
        AssertProblem(
            await NewController(factory).List(Guid.NewGuid(), runId, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task List_ReturnsLogsNewestFileTimeFirst()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);

        await SeedLogAsync(factory, runId, "older",  DateTimeOffset.UtcNow.AddHours(-1));
        await SeedLogAsync(factory, runId, "newer",  DateTimeOffset.UtcNow);

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).List(jobId, runId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<LogSummaryDto>>(ok.Value).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "newer", "older" }, rows.Select(l => l.ShotName));
        Assert.All(rows, r => Assert.NotNull(r.RowVersion));
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_UnknownLog_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        AssertProblem(
            await NewController(factory).Get(jobId, runId, 99999, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Get_ReturnsDetailWithRowVersion()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var logId = await SeedLogAsync(factory, runId, "alpha");

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).Get(jobId, runId, logId, CancellationToken.None));
        var dto = Assert.IsType<LogDetailDto>(ok.Value);
        Assert.Equal("alpha", dto.ShotName);
        Assert.NotNull(dto.RowVersion);
    }

    // ============================================================
    // Create
    // ============================================================

    [Fact]
    public async Task Create_UnknownRun_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, _) = await SeedJobAndRunAsync(factory);

        var dto = new CreateLogDto("shot", DateTimeOffset.UtcNow);
        AssertProblem(
            await NewController(factory).Create(jobId, Guid.NewGuid(), dto, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Create_ValidPayload_PersistsAndReturnsCreated()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var calId = await SeedCalibrationAsync(factory);

        var dto = new CreateLogDto("shotX", DateTimeOffset.UtcNow, CalibrationId: calId);
        var result = await NewController(factory).Create(jobId, runId, dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<LogDetailDto>(created.Value);
        Assert.Equal("shotX", detail.ShotName);
        Assert.Equal(calId, detail.CalibrationId);
        Assert.False(detail.HasBinary);
        Assert.Empty(detail.ResultFiles);
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_UnknownLog_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);

        var dto = new UpdateLogDto("x", DateTimeOffset.UtcNow, null, TestRowVersion);
        AssertProblem(
            await NewController(factory).Update(jobId, runId, 99999, dto, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Update_MissingRowVersion_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var logId = await SeedLogAsync(factory, runId);

        var dto = new UpdateLogDto("x", DateTimeOffset.UtcNow, null, RowVersion: null);
        AssertProblem(
            await NewController(factory).Update(jobId, runId, logId, dto, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Update_MalformedRowVersion_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var logId = await SeedLogAsync(factory, runId);

        var dto = new UpdateLogDto("x", DateTimeOffset.UtcNow, null,
                                   RowVersion: "NOT-VALID-BASE64@");
        AssertProblem(
            await NewController(factory).Update(jobId, runId, logId, dto, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Update_ValidPayload_Persists()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var logId = await SeedLogAsync(factory, runId, shotName: "old");
        var calId = await SeedCalibrationAsync(factory);

        var dto = new UpdateLogDto("new", DateTimeOffset.UtcNow, calId, TestRowVersion);
        Assert.IsType<NoContentResult>(
            await NewController(factory).Update(jobId, runId, logId, dto, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Logs.AsNoTracking().FirstAsync(l => l.Id == logId);
        Assert.Equal("new", reloaded.ShotName);
        Assert.Equal(calId, reloaded.CalibrationId);
    }

    /// <summary>
    /// Seed a tenant Calibration row matching the post-issue-#26
    /// snapshot shape. Returns its <c>Id</c> so tests can stamp it
    /// onto Shot/Log <c>CalibrationId</c> without tripping the
    /// CalibrationFkValidation guard.
    /// </summary>
    private static async Task<int> SeedCalibrationAsync(FakeTenantDbContextFactory factory)
    {
        await using var db = factory.NewActiveContext();
        var cal = new Calibration
        {
            MasterCalibrationId = Guid.NewGuid(),
            ToolId              = Guid.NewGuid(),
            SerialNumber        = 9999,
            CalibrationDate     = DateTimeOffset.UtcNow,
            PayloadJson         = "{}",
            MagnetometerCount   = 3,
        };
        db.Calibrations.Add(cal);
        await db.SaveChangesAsync();
        return cal.Id;
    }

    // ============================================================
    // Delete
    // ============================================================

    [Fact]
    public async Task Delete_UnknownLog_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        AssertProblem(
            await NewController(factory).Delete(jobId, runId, 99999, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var logId = await SeedLogAsync(factory, runId);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Delete(jobId, runId, logId, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        Assert.False(await verify.Logs.AnyAsync(l => l.Id == logId));
    }
}
