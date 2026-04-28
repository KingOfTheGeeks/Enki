using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Comments;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Comments;
using SDI.Enki.Shared.Shots;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="ShotsController"/>. Same
/// shape as <c>RunsControllerTests</c> + <c>LogsControllerTests</c>:
/// fake tenant-DB factory backed by InMemory; bypass auth + tenant
/// routing; cover identity CRUD + binary upload/download (primary +
/// gyro) + config + comments subresource + concurrency.
///
/// <para>
/// Phase 2 acceptance: every binary upload / config change must flip
/// the matching <c>ResultStatus</c> to <c>Pending</c> — that's the
/// calc seam Marduk reads. Several tests below verify that flag
/// flip explicitly.
/// </para>
/// </summary>
public class ShotsControllerTests
{
    private const string TestTenantCode = "ACME";

    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    private static ShotsController NewController(FakeTenantDbContextFactory factory)
    {
        var controller = new ShotsController(factory);
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

    /// <summary>
    /// Build a minimal <see cref="IFormFile"/> wrapping <paramref name="bytes"/>.
    /// Mirrors the helper in <c>RunsControllerTests</c>.
    /// </summary>
    private static IFormFile MakeFormFile(byte[] bytes, string fileName)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, baseStreamOffset: 0, length: bytes.Length,
                            name: "file", fileName: fileName);
    }

    private static async Task<(Guid jobId, Guid runId)> SeedJobAndRunAsync(
        FakeTenantDbContextFactory factory,
        RunType? runType = null)
    {
        await using var db = factory.NewActiveContext();
        var job = new Job("Job-A", "for shots", UnitSystem.Field) { RowVersion = TestRowVersionBytes };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = new Run("Run-A", "the run", 0, 100, runType ?? RunType.Gradient)
        {
            JobId      = job.Id,
            Status     = RunStatus.Active,
            RowVersion = TestRowVersionBytes,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return (job.Id, run.Id);
    }

    private static async Task<int> SeedShotAsync(
        FakeTenantDbContextFactory factory,
        Guid runId,
        string shotName = "shot-1",
        DateTimeOffset? fileTime = null)
    {
        await using var db = factory.NewActiveContext();
        var shot = new Shot
        {
            RunId = runId,
            ShotName = shotName,
            FileTime = fileTime ?? DateTimeOffset.UtcNow,
            RowVersion = TestRowVersionBytes,
        };
        db.Shots.Add(shot);
        await db.SaveChangesAsync();
        return shot.Id;
    }

    // ============================================================
    // List / Get
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
    public async Task List_ReturnsShotsNewestFileTimeFirst()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);

        await SeedShotAsync(factory, runId, "older", DateTimeOffset.UtcNow.AddHours(-1));
        await SeedShotAsync(factory, runId, "newer", DateTimeOffset.UtcNow);

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).List(jobId, runId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<ShotSummaryDto>>(ok.Value).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "newer", "older" }, rows.Select(s => s.ShotName));
        Assert.All(rows, r => Assert.False(r.HasBinary));
        Assert.All(rows, r => Assert.False(r.HasGyroBinary));
    }

    [Fact]
    public async Task Get_UnknownShot_ReturnsNotFoundProblem()
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
        var shotId = await SeedShotAsync(factory, runId, "alpha");

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).Get(jobId, runId, shotId, CancellationToken.None));
        var dto = Assert.IsType<ShotDetailDto>(ok.Value);
        Assert.Equal("alpha", dto.ShotName);
        Assert.False(dto.HasBinary);
        Assert.False(dto.HasGyroBinary);
        Assert.NotNull(dto.RowVersion);
    }

    // ============================================================
    // Create / Update
    // ============================================================

    [Fact]
    public async Task Create_UnknownRun_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, _) = await SeedJobAndRunAsync(factory);

        var dto = new CreateShotDto("shot", DateTimeOffset.UtcNow);
        AssertProblem(
            await NewController(factory).Create(jobId, Guid.NewGuid(), dto, CancellationToken.None),
            404, "/not-found");
    }

    [Fact]
    public async Task Create_PersistsIdentityOnly()
    {
        // Phase-2 reshape: CreateShotDto is identity-only — no
        // calibration (run-based via Tool) and no config (server-built
        // typed Marduk class). All capture/calc fields land later
        // through dedicated endpoints.
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);

        var dto = new CreateShotDto("shotX", DateTimeOffset.UtcNow);
        var result = await NewController(factory).Create(jobId, runId, dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var detail = Assert.IsType<ShotDetailDto>(created.Value);
        Assert.Equal("shotX", detail.ShotName);
        Assert.False(detail.HasBinary);
        Assert.Null(detail.ConfigJson);
        Assert.Null(detail.ConfigUpdatedAt);
        Assert.Null(detail.ResultStatus);
    }

    [Fact]
    public async Task Update_MissingRowVersion_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        var dto = new UpdateShotDto("shotX", DateTimeOffset.UtcNow, null, RowVersion: null);
        AssertProblem(
            await NewController(factory).Update(jobId, runId, shotId, dto, CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task Update_ValidPayload_PersistsIdentityColumns()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId, shotName: "old");

        var dto = new UpdateShotDto("new", DateTimeOffset.UtcNow, 11, TestRowVersion);
        Assert.IsType<NoContentResult>(
            await NewController(factory).Update(jobId, runId, shotId, dto, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Shots.AsNoTracking().FirstAsync(s => s.Id == shotId);
        Assert.Equal("new", reloaded.ShotName);
        Assert.Equal(11, reloaded.CalibrationId);
    }

    // ============================================================
    // Primary binary upload / download / delete
    // ============================================================

    [Fact]
    public async Task UploadBinary_EmptyFile_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        AssertProblem(
            await NewController(factory).UploadBinary(jobId, runId, shotId,
                MakeFormFile(Array.Empty<byte>(), "empty.bin"), CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task UploadBinary_OverLimit_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        var oversized = new byte[ShotsController.MaxBinaryBytes + 1];
        AssertProblem(
            await NewController(factory).UploadBinary(jobId, runId, shotId,
                MakeFormFile(oversized, "huge.bin"), CancellationToken.None),
            400, "/validation");
    }

    [Fact]
    public async Task UploadBinary_PersistsBytesAndFlipsResultToPending()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        Assert.IsType<NoContentResult>(
            await NewController(factory).UploadBinary(jobId, runId, shotId,
                MakeFormFile(bytes, "shot.bin"), CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Shots.AsNoTracking().FirstAsync(s => s.Id == shotId);
        Assert.Equal(bytes, reloaded.Binary);
        Assert.Equal("shot.bin", reloaded.BinaryName);
        Assert.NotNull(reloaded.BinaryUploadedAt);
        // Calc seam
        Assert.Equal("Pending", reloaded.ResultStatus);
    }

    [Fact]
    public async Task DownloadBinary_MissingBinary_Returns404()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        AssertProblem(
            await NewController(factory).DownloadBinary(jobId, runId, shotId, CancellationToken.None),
            404, "/not-found");
    }

    // ============================================================
    // Gyro binary — only valid with a primary on file
    // ============================================================

    [Fact]
    public async Task UploadGyroBinary_NoPrimary_ReturnsConflict()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        AssertProblem(
            await NewController(factory).UploadGyroBinary(jobId, runId, shotId,
                MakeFormFile(new byte[] { 1, 2 }, "gyro.bin"), CancellationToken.None),
            409, "/conflict");
    }

    [Fact]
    public async Task UploadGyroBinary_WithPrimary_PersistsAndFlipsToPending()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        // Seed primary binary first.
        await using (var db = factory.NewActiveContext())
        {
            var s = await db.Shots.FirstAsync(x => x.Id == shotId);
            s.Binary = new byte[] { 1, 2, 3 };
            await db.SaveChangesAsync();
        }

        var gyroBytes = new byte[] { 9, 9, 9 };
        Assert.IsType<NoContentResult>(
            await NewController(factory).UploadGyroBinary(jobId, runId, shotId,
                MakeFormFile(gyroBytes, "gyro.bin"), CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        var reloaded = await verify.Shots.AsNoTracking().FirstAsync(s => s.Id == shotId);
        Assert.Equal(gyroBytes, reloaded.GyroBinary);
        Assert.Equal("Pending", reloaded.GyroResultStatus);
    }

    // ============================================================
    // Config — calc seam test
    // ============================================================

    [Fact]
    public async Task SetConfig_PersistsJsonAndStatusOnlyFlipsWithBinary()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        // No binary yet — config persists, status stays null (nothing to compute against).
        Assert.IsType<NoContentResult>(
            await NewController(factory).SetConfig(jobId, runId, shotId,
                "{\"mode\":\"a\"}", CancellationToken.None));

        await using (var verify = factory.NewActiveContext())
        {
            var reloaded = await verify.Shots.AsNoTracking().FirstAsync(s => s.Id == shotId);
            Assert.Equal("{\"mode\":\"a\"}", reloaded.ConfigJson);
            Assert.Null(reloaded.ResultStatus);
        }

        // Add binary, set config again — now status flips to Pending.
        await using (var db = factory.NewActiveContext())
        {
            var s = await db.Shots.FirstAsync(x => x.Id == shotId);
            s.Binary = new byte[] { 1, 2 };
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(
            await NewController(factory).SetConfig(jobId, runId, shotId,
                "{\"mode\":\"b\"}", CancellationToken.None));

        await using var final = factory.NewActiveContext();
        var afterBoth = await final.Shots.AsNoTracking().FirstAsync(s => s.Id == shotId);
        Assert.Equal("{\"mode\":\"b\"}", afterBoth.ConfigJson);
        Assert.Equal("Pending", afterBoth.ResultStatus);
    }

    // ============================================================
    // Comments subresource (1:N — Phase 2 reparented from m:n)
    // ============================================================

    [Fact]
    public async Task AddComment_PersistsAndReturnsCreated()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        var result = await NewController(factory).AddComment(
            jobId, runId, shotId,
            new CreateCommentDto("Got a noisy mag at this stand."),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<CommentDto>(created.Value);
        Assert.Equal(shotId, dto.ShotId);
        Assert.Equal("Got a noisy mag at this stand.", dto.Text);

        await using var verify = factory.NewActiveContext();
        Assert.Single(verify.Comments.Where(c => c.ShotId == shotId));
    }

    [Fact]
    public async Task ListComments_ReturnsNewestFirst()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        await using (var db = factory.NewActiveContext())
        {
            db.Comments.Add(new Comment(shotId, "older", "alice")
            {
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
            db.Comments.Add(new Comment(shotId, "newer", "bob")
            {
                Timestamp = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ok = Assert.IsType<OkObjectResult>(
            await NewController(factory).ListComments(jobId, runId, shotId, CancellationToken.None));
        var rows = Assert.IsAssignableFrom<IEnumerable<CommentDto>>(ok.Value).ToList();
        Assert.Equal(new[] { "newer", "older" }, rows.Select(c => c.Text));
    }

    // ============================================================
    // Delete
    // ============================================================

    [Fact]
    public async Task Delete_RemovesShot()
    {
        var factory = new FakeTenantDbContextFactory();
        var (jobId, runId) = await SeedJobAndRunAsync(factory);
        var shotId = await SeedShotAsync(factory, runId);

        Assert.IsType<NoContentResult>(
            await NewController(factory).Delete(jobId, runId, shotId, CancellationToken.None));

        await using var verify = factory.NewActiveContext();
        Assert.False(await verify.Shots.AnyAsync(s => s.Id == shotId));
    }
}
