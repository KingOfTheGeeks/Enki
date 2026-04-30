using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Logs;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Runs;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="RunsController"/>.
/// Same shape as <c>JobsControllerTests</c> + <c>WellsControllerTests</c>:
/// fake tenant-DB factory backed by InMemory; bypass auth /
/// tenant-routing middleware; cover CRUD + lifecycle + soft-delete +
/// concurrency.
/// </summary>
public class RunsControllerTests
{
    private const string TestTenantCode = "ACME";

    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    private static RunsController NewController(FakeTenantDbContextFactory factory)
    {
        var controller = new RunsController(factory);

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["tenantCode"] = TestTenantCode;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData   = routeData,
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

    private static async Task<Guid> SeedJobAsync(FakeTenantDbContextFactory factory)
    {
        await using var db = factory.NewActiveContext();
        var job = new Job("Job-A", "for runs", UnitSystem.Field) { RowVersion = TestRowVersionBytes };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task<Run> SeedRunAsync(
        FakeTenantDbContextFactory factory,
        Guid jobId,
        string name = "Test Run",
        RunType? type = null,
        RunStatus? status = null,
        DateTimeOffset? createdAt = null)
    {
        await using var db = factory.NewActiveContext();
        var run = new Run(name, "desc", startDepth: 1000, endDepth: 2000,
                          type: type ?? RunType.Gradient)
        {
            JobId      = jobId,
            Status     = status ?? RunStatus.Planned,
            CreatedAt  = createdAt ?? DateTimeOffset.UtcNow,
            RowVersion = TestRowVersionBytes,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_UnknownJob_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);
        AssertProblem(await sut.List(Guid.NewGuid(), CancellationToken.None), 404, "/not-found");
    }

    [Fact]
    public async Task List_ReturnsRunsNewestFirst_ExcludesArchived()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        await SeedRunAsync(factory, jobId, name: "Older",  createdAt: DateTimeOffset.UtcNow.AddDays(-3));
        await SeedRunAsync(factory, jobId, name: "Newer",  createdAt: DateTimeOffset.UtcNow);

        // Archived run — must not appear in the default list.
        await using (var db = factory.NewActiveContext())
        {
            var dead = new Run("Dead", "archived", 0, 0, RunType.Rotary)
            {
                JobId      = jobId,
                ArchivedAt = DateTimeOffset.UtcNow,
                RowVersion = TestRowVersionBytes,
            };
            db.Runs.Add(dead);
            await db.SaveChangesAsync();
        }

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).List(jobId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<RunSummaryDto>>(ok.Value).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Newer", "Older" }, rows.Select(r => r.Name));
        Assert.DoesNotContain(rows, r => r.Name == "Dead");
    }

    [Fact]
    public async Task ListArchived_ReturnsOnlyArchivedRunsForJob()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        await SeedRunAsync(factory, jobId, name: "Live");

        Run dead;
        await using (var db = factory.NewActiveContext())
        {
            dead = new Run("Dead", "archived", 0, 0, RunType.Rotary)
            {
                JobId      = jobId,
                ArchivedAt = DateTimeOffset.UtcNow,
                RowVersion = TestRowVersionBytes,
            };
            db.Runs.Add(dead);
            await db.SaveChangesAsync();
        }

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).ListArchived(jobId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<RunSummaryDto>>(ok.Value).ToList();

        Assert.Single(rows);
        Assert.Equal(dead.Id, rows[0].Id);
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_UnknownRun_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        AssertProblem(
            await NewController(factory).Get(jobId, Guid.NewGuid(), CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Get_ArchivedRun_FilteredOutAs404()
    {
        // Default Get goes through the global query filter; an
        // archived run looks "deleted" to the user.
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        Run dead;
        await using (var db = factory.NewActiveContext())
        {
            dead = new Run("Dead", "archived", 0, 0, RunType.Rotary)
            {
                JobId      = jobId,
                ArchivedAt = DateTimeOffset.UtcNow,
                RowVersion = TestRowVersionBytes,
            };
            db.Runs.Add(dead);
            await db.SaveChangesAsync();
        }

        AssertProblem(
            await NewController(factory).Get(jobId, dead.Id, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Get_ReturnsDetailWithRowVersionAndLogCount()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, name: "G-1", type: RunType.Gradient);

        await using (var db = factory.NewActiveContext())
        {
            db.Logs.Add(new Log(run.Id, "shotA", DateTimeOffset.UtcNow));
            db.Logs.Add(new Log(run.Id, "shotB", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).Get(jobId, run.Id, CancellationToken.None));
        var dto = Assert.IsType<RunDetailDto>(ok.Value);
        Assert.Equal("G-1", dto.Name);
        Assert.Equal("Gradient", dto.Type);
        Assert.Equal(2, dto.LogCount);
        Assert.NotNull(dto.RowVersion);
    }

    // ============================================================
    // Create
    // ============================================================

    [Fact]
    public async Task Create_UnknownJob_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new CreateRunDto("R", "desc", "Gradient", 0, 100);
        AssertProblem(
            await sut.Create(Guid.NewGuid(), dto, CancellationToken.None),
            404, "/not-found");
    }

    [Theory]
    [InlineData("Gradient")]
    [InlineData("Rotary")]
    [InlineData("Passive")]
    public async Task Create_ValidType_PersistsAndReturnsCreated(string type)
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var sut = NewController(factory);

        var dto = new CreateRunDto("R-" + type, "desc", type, 100, 200);
        var result = await sut.Create(jobId, dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<RunDetailDto>(created.Value);
        Assert.Equal(type, detail.Type);
        Assert.Equal("Planned", detail.Status);
    }

    [Fact]
    public async Task Create_UnknownType_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        var dto = new CreateRunDto("R", "desc", "NotARunType", 0, 100);
        AssertProblem(
            await NewController(factory).Create(jobId, dto, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Create_NonGradient_GradientOnlyFieldsAreCleared()
    {
        // BridleLength + CurrentInjection are Gradient-only; if a chatty
        // client sends them on a Rotary or Passive run they're ignored.
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        var dto = new CreateRunDto("R-Rotary", "desc", "Rotary", 0, 100,
                                   BridleLength: 5.0, CurrentInjection: 10.0);
        var result = await NewController(factory).Create(jobId, dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<RunDetailDto>(created.Value);
        Assert.Null(detail.BridleLength);
        Assert.Null(detail.CurrentInjection);
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_UnknownRun_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        var dto = new UpdateRunDto("X", "y", 0, 100, null, null, null, null, null, TestRowVersion);
        AssertProblem(
            await NewController(factory).Update(jobId, Guid.NewGuid(), dto, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Update_MissingRowVersion_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId);

        var dto = new UpdateRunDto("X", "y", 0, 100, null, null, null, null, null, RowVersion: null);
        AssertProblem(
            await NewController(factory).Update(jobId, run.Id, dto, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Update_MalformedRowVersion_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId);

        var dto = new UpdateRunDto("X", "y", 0, 100, null, null, null, null, null,
                                   RowVersion: "NOT-VALID-BASE64@");
        AssertProblem(
            await NewController(factory).Update(jobId, run.Id, dto, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Update_TerminalStatus_ReturnsConflictProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, status: RunStatus.Completed);

        var dto = new UpdateRunDto("X", "y", 0, 100, null, null, null, null, null, TestRowVersion);
        AssertProblem(
            await NewController(factory).Update(jobId, run.Id, dto, CancellationToken.None),
            409, "/conflict");
    }

    [Fact]
    public async Task Update_ValidPayload_Persists()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, name: "Old");

        var dto = new UpdateRunDto("New", "new desc", 100, 200, null, null, null, null, null, TestRowVersion);
        Assert.IsType<NoContentResult>(
            await NewController(factory).Update(jobId, run.Id, dto, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal("New", reloaded.Name);
        Assert.Equal(100, reloaded.StartDepth);
    }

    // ============================================================
    // Lifecycle
    // ============================================================

    [Fact]
    public async Task Start_Planned_TransitionsToActive()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, status: RunStatus.Planned);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Start(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task SameStatusTransition_IsIdempotent_Returns204()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, status: RunStatus.Active);

        // Active → Active is a no-op.
        Assert.IsType<NoContentResult>(
            await NewController(factory).Start(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));
    }

    [Fact]
    public async Task IllegalTransition_ReturnsConflictProblem()
    {
        // Cancelled is terminal — no allowed transitions out of it.
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, status: RunStatus.Cancelled);

        AssertProblem(
            await NewController(factory).Start(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None),
            409, "/conflict");
    }

    [Fact]
    public async Task SuspendThenResume_RoundTripsThroughActive()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, status: RunStatus.Active);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Suspend(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));
        Assert.IsType<NoContentResult>(
            await NewController(factory).Start(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task Complete_FromActive_TransitionsToCompleted()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, status: RunStatus.Active);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Complete(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Completed, reloaded.Status);
    }

    // ============================================================
    // Soft-delete
    // ============================================================

    [Fact]
    public async Task Delete_ActiveRun_SoftArchivesRow()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Delete(jobId, run.Id, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        Assert.False(await verify.Runs.AnyAsync(r => r.Id == run.Id)); // hidden by filter
        var archived = await verify.Runs.IgnoreQueryFilters().SingleAsync(r => r.Id == run.Id);
        Assert.NotNull(archived.ArchivedAt);
    }

    [Fact]
    public async Task Delete_AlreadyArchived_Returns404()
    {
        // Same shape as Wells: filter hides the archived row from the
        // controller's lookup; user with a stale URL gets 404.
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId);

        await NewController(factory).Delete(jobId, run.Id, CancellationToken.None);

        AssertProblem(
            await NewController(factory).Delete(jobId, run.Id, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Restore_ArchivedRun_ClearsArchivedAt()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId);

        await NewController(factory).Delete(jobId, run.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(
            await NewController(factory).Restore(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.SingleAsync(r => r.Id == run.Id);
        Assert.Null(reloaded.ArchivedAt);
    }

    [Fact]
    public async Task Restore_AlreadyActive_IsIdempotent()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Restore(jobId, run.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None));
    }

    [Fact]
    public async Task Restore_UnknownRun_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);

        AssertProblem(
            await NewController(factory).Restore(jobId, Guid.NewGuid(), new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None),
            404, "/not-found");
    }

    // ============================================================
    // Passive binary + config (Phase 2 — Passive runs only)
    // ============================================================

    /// <summary>
    /// Build a fake <see cref="IFormFile"/> wrapping the supplied bytes.
    /// Doesn't depend on FormFileCollection / multipart parsing — the
    /// controller calls only Length, FileName, and CopyToAsync on the
    /// IFormFile, all of which the framework's FormFile implementation
    /// satisfies straight from a backing stream.
    /// </summary>
    private static IFormFile MakeFormFile(byte[] bytes, string fileName)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, baseStreamOffset: 0, length: bytes.Length,
                            name: "file", fileName: fileName);
    }

    [Fact]
    public async Task UploadPassiveBinary_NonPassiveRun_ReturnsConflict()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Gradient);

        var file = MakeFormFile(new byte[] { 1, 2, 3 }, "p.bin");
        AssertProblem(
            await NewController(factory).UploadPassiveBinary(jobId, run.Id, file, CancellationToken.None),
            409, "/conflict");
    }

    [Fact]
    public async Task UploadPassiveBinary_OnPassive_PersistsBytesAndFlipsToPending()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var file = MakeFormFile(bytes, "passive.bin");

        Assert.IsType<NoContentResult>(
            await NewController(factory).UploadPassiveBinary(jobId, run.Id, file, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(bytes, reloaded.PassiveBinary);
        Assert.Equal("passive.bin", reloaded.PassiveBinaryName);
        Assert.NotNull(reloaded.PassiveBinaryUploadedAt);
        // Calc seam: upload triggers Pending so a future calc service
        // knows there's work to do.
        Assert.Equal("Pending", reloaded.PassiveResultStatus);
    }

    [Fact]
    public async Task UploadPassiveBinary_EmptyFile_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        var file = MakeFormFile(Array.Empty<byte>(), "empty.bin");
        AssertProblem(
            await NewController(factory).UploadPassiveBinary(jobId, run.Id, file, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task DownloadPassiveBinary_MissingBinary_Returns404()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        // Run exists, but no binary uploaded.
        AssertProblem(
            await NewController(factory).DownloadPassiveBinary(jobId, run.Id, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task DownloadPassiveBinary_PresentBinary_ReturnsFile()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await using (var db = factory.NewActiveContext())
        {
            var fresh = await db.Runs.FirstAsync(r => r.Id == run.Id);
            fresh.PassiveBinary = bytes;
            fresh.PassiveBinaryName = "p.bin";
            fresh.PassiveBinaryUploadedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var result = await NewController(factory).DownloadPassiveBinary(jobId, run.Id, CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(bytes, file.FileContents);
        Assert.Equal("p.bin", file.FileDownloadName);
    }

    [Fact]
    public async Task DeletePassiveBinary_NonPassiveRun_ReturnsConflict()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Rotary);

        AssertProblem(
            await NewController(factory).DeletePassiveBinary(jobId, run.Id, CancellationToken.None),
            409, "/conflict");
    }

    [Fact]
    public async Task DeletePassiveBinary_ClearsBinaryAndResult()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        await using (var db = factory.NewActiveContext())
        {
            var fresh = await db.Runs.FirstAsync(r => r.Id == run.Id);
            fresh.PassiveBinary = new byte[] { 1, 2 };
            fresh.PassiveBinaryName = "p.bin";
            fresh.PassiveResultStatus = "Pending";
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(
            await NewController(factory).DeletePassiveBinary(jobId, run.Id, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Null(reloaded.PassiveBinary);
        Assert.Null(reloaded.PassiveBinaryName);
        Assert.Null(reloaded.PassiveResultStatus);
    }

    [Fact]
    public async Task SetPassiveConfig_NonPassiveRun_ReturnsConflict()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Gradient);

        AssertProblem(
            await NewController(factory).SetPassiveConfig(
                jobId, run.Id, "{\"k\":\"v\"}", CancellationToken.None),
            409, "/conflict");
    }

    [Fact]
    public async Task SetPassiveConfig_NoBinary_PersistsConfigAndStatusStaysNull()
    {
        // Without a binary on file there's nothing to compute against,
        // so SetConfig persists the JSON but doesn't flip status to
        // Pending — same shape as Shot.SetConfig.
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        Assert.IsType<NoContentResult>(
            await NewController(factory).SetPassiveConfig(
                jobId, run.Id, "{\"mode\":\"x\"}", CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal("{\"mode\":\"x\"}", reloaded.PassiveConfigJson);
        Assert.NotNull(reloaded.PassiveConfigUpdatedAt);
        Assert.Null(reloaded.PassiveResultStatus);
    }

    [Fact]
    public async Task SetPassiveConfig_WithBinary_PersistsConfigAndFlipsToPending()
    {
        var factory = new FakeTenantDbContextFactory();
        var jobId = await SeedJobAsync(factory);
        var run = await SeedRunAsync(factory, jobId, type: RunType.Passive);

        await using (var db = factory.NewActiveContext())
        {
            var fresh = await db.Runs.FirstAsync(r => r.Id == run.Id);
            fresh.PassiveBinary = new byte[] { 1, 2, 3 };
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(
            await NewController(factory).SetPassiveConfig(
                jobId, run.Id, "{\"mode\":\"y\"}", CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal("{\"mode\":\"y\"}", reloaded.PassiveConfigJson);
        Assert.Equal("Pending", reloaded.PassiveResultStatus);
    }
}
