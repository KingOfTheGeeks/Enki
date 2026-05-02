using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="JobsController"/>. Uses a fake
/// <see cref="FakeTenantDbContextFactory"/> backed by EF InMemory — avoids
/// spinning up the routing middleware and authorization pipeline for
/// tests that only care about the controller's action logic.
///
/// <para>
/// The authorization policy (<c>CanAccessTenant</c>) and tenant-code route
/// resolution are covered at the integration layer in a later phase; here
/// we bypass both by constructing the controller directly and giving it a
/// stub <see cref="HttpContext"/> with the <c>tenantCode</c> route value
/// pre-populated so <c>CreatedAtAction</c> can build its Location header.
/// </para>
/// </summary>
public class JobsControllerTests
{
    // ---------- fixture helpers ----------

    private const string TestTenantCode = "ACME";

    private static JobsController NewController(FakeTenantDbContextFactory factory)
    {
        var controller = new JobsController(factory);

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
        Assert.NotNull(problem.Extensions["traceId"]);
    }

    private static Job SeedJob(
        FakeTenantDbContextFactory factory,
        string name          = "Test Job",
        string description   = "A job",
        UnitSystem? system   = null,
        JobStatus? status    = null,
        string? wellName     = null,
        string? region       = null,
        DateTimeOffset? createdAt = null)
    {
        using var db = factory.NewActiveContext();
        var job = new Job(name, description, system ?? UnitSystem.Field)
        {
            Status         = status ?? JobStatus.Draft,
            WellName       = wellName,
            Region         = region,
            CreatedAt      = createdAt ?? DateTimeOffset.UtcNow,
            StartTimestamp = DateTimeOffset.UtcNow,
            EndTimestamp   = DateTimeOffset.UtcNow.AddMonths(1),
            RowVersion     = TestRowVersionBytes,
        };
        db.Jobs.Add(job);
        db.SaveChanges();
        return job;
    }

    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_NoJobs_ReturnsEmpty()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var result = await sut.List(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task List_ReturnsJobsNewestFirst()
    {
        var factory = new FakeTenantDbContextFactory();
        // Seed deliberately out-of-order so ordering isn't an insertion artefact.
        SeedJob(factory, name: "Old",    createdAt: DateTimeOffset.UtcNow.AddDays(-10));
        SeedJob(factory, name: "Middle", createdAt: DateTimeOffset.UtcNow.AddDays(-3));
        SeedJob(factory, name: "Newest", createdAt: DateTimeOffset.UtcNow);

        var sut = NewController(factory);
        var result = (await sut.List(CancellationToken.None)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "Newest", "Middle", "Old" }, result.Select(j => j.Name));
    }

    [Fact]
    public async Task List_ProjectsAllSummaryFields()
    {
        var factory = new FakeTenantDbContextFactory();
        SeedJob(factory,
            name:        "North Slope 22H",
            description: "Horizontal well, 22-section",
            system:      UnitSystem.Metric,
            status:      JobStatus.Active,
            wellName:    "NS-22",
            region:      "North Slope");

        var sut = NewController(factory);
        var row = Assert.Single(await sut.List(CancellationToken.None));

        Assert.Equal("North Slope 22H", row.Name);
        Assert.Equal("NS-22",            row.WellName);
        Assert.Equal("North Slope",      row.Region);
        Assert.Equal("Metric",           row.UnitSystem);
        Assert.Equal("Active",           row.Status);
    }

    // ============================================================
    // Get
    // ============================================================

    [Fact]
    public async Task Get_KnownId_ReturnsDetail()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, name: "Detail Job", region: "Permian Basin");
        var sut = NewController(factory);

        var result = await sut.Get(seeded.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<JobDetailDto>(ok.Value);
        Assert.Equal(seeded.Id,       dto.Id);
        Assert.Equal("Detail Job",    dto.Name);
        Assert.Equal("Permian Basin", dto.Region);
        Assert.Equal("Draft",         dto.Status);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var unknownId = Guid.NewGuid();
        var result = await sut.Get(unknownId, CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
        var problem = (ProblemDetails)((ObjectResult)result).Value!;
        Assert.Equal("Job",                problem.Extensions["entityKind"]);
        Assert.Equal(unknownId.ToString(), problem.Extensions["entityKey"]);
    }

    // ============================================================
    // Create
    // ============================================================

    [Fact]
    public async Task Create_ValidRequest_PersistsJobAndReturnsCreated()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new CreateJobDto(
            Name:        "New Job",
            Description: "Fresh from the API",
            UnitSystem:  "Field",
            WellName:    "Lone Star 14H",
            Region:      "Bakken");

        var result = await sut.Create(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(JobsController.Get), created.ActionName);
        Assert.Equal(TestTenantCode, created.RouteValues?["tenantCode"]);
        var body = Assert.IsType<JobDetailDto>(created.Value);
        Assert.Equal("New Job", body.Name);
        Assert.Equal("Bakken",  body.Region);
        Assert.Equal("Draft",   body.Status);
        Assert.Equal("Field",   body.UnitSystem);

        // Verify it landed in the store.
        using var db = factory.NewActiveContext();
        var persisted = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == body.Id);
        Assert.Equal("New Job",         persisted.Name);
        Assert.Equal("Bakken",          persisted.Region);
        Assert.Equal(UnitSystem.Field,  persisted.UnitSystem);
    }

    [Fact]
    public async Task Create_DefaultsTimestamps_WhenDtoHasNone()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new CreateJobDto("No Dates", "Timestamps should default", "Metric");

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var result = await sut.Create(dto, CancellationToken.None);
        var after  = DateTimeOffset.UtcNow.AddSeconds(1);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<JobDetailDto>(created.Value);
        Assert.InRange(body.StartTimestamp, before, after);
        Assert.InRange(body.EndTimestamp,   before, after);
    }

    [Fact]
    public async Task Create_UnknownUnitSystem_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new CreateJobDto("Bad", "desc", UnitSystem: "Imperial");

        var result = await sut.Create(dto, CancellationToken.None);

        AssertProblem(result, 400, "/validation");
        var problem = (ProblemDetails)((ObjectResult)result).Value!;
        Assert.NotNull(problem.Extensions["errors"]);
    }

    [Fact]
    public async Task Create_UnitSystem_IsCaseInsensitive()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new CreateJobDto("Lower", "lower-case units", UnitSystem: "metric");

        var result = await sut.Create(dto, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task Create_CustomUnitSystem_IsRejected()
    {
        // Custom is reserved for the per-user override phase — accepting
        // it today would leave the server with no resolver for display
        // units, so 400 until that plumbing lands.
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new CreateJobDto("Custom", "desc", UnitSystem: "Custom");

        var result = await sut.Create(dto, CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_ValidRequest_UpdatesFields()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, name: "Original", region: "North Sea");
        var sut = NewController(factory);

        var dto = new UpdateJobDto(
            Name:        "Renamed",
            Description: "Updated description",
            UnitSystem:  "Metric",
            WellName:    "W-42",
            Region:      "Gulf of Mexico",
            RowVersion:  TestRowVersion);

        var result = await sut.Update(seeded.Id, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal("Renamed",             reloaded.Name);
        Assert.Equal("Updated description", reloaded.Description);
        Assert.Equal(UnitSystem.Metric,     reloaded.UnitSystem);
        Assert.Equal("W-42",                reloaded.WellName);
        Assert.Equal("Gulf of Mexico",      reloaded.Region);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var dto = new UpdateJobDto(Name: "whatever", Description: "x", UnitSystem: "Field", RowVersion: TestRowVersion);
        var result = await sut.Update(Guid.NewGuid(), dto, CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Update_ArchivedJob_ReturnsConflictProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Archived);
        var sut = NewController(factory);

        var dto = new UpdateJobDto("new", "new", "Field", RowVersion: TestRowVersion);
        var result = await sut.Update(seeded.Id, dto, CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
    }

    [Fact]
    public async Task Update_UnknownUnitSystem_ReturnsValidationProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory);
        var sut = NewController(factory);

        var dto = new UpdateJobDto(Name: "x", Description: "y", UnitSystem: "Rods", RowVersion: TestRowVersion);
        var result = await sut.Update(seeded.Id, dto, CancellationToken.None);

        AssertProblem(result, 400, "/validation");
    }

    [Fact]
    public async Task Update_ClearsOptionalFields_WhenDtoFieldsAreNull()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, wellName: "old-well", region: "old-region");
        var sut = NewController(factory);

        var dto = new UpdateJobDto(
            Name:        seeded.Name,
            Description: seeded.Description,
            UnitSystem:  "Field",
            WellName:    null,
            Region:      null,
            RowVersion:  TestRowVersion);

        var result = await sut.Update(seeded.Id, dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Null(reloaded.WellName);
        Assert.Null(reloaded.Region);
    }

    // ============================================================
    // Archive
    // ============================================================

    [Fact]
    public async Task Archive_DraftJob_SetsStatusArchived()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Draft);
        var sut = NewController(factory);

        var result = await sut.Archive(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal(JobStatus.Archived, reloaded.Status);
    }

    [Fact]
    public async Task Archive_ActiveJob_SetsStatusArchived()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Active);
        var sut = NewController(factory);

        var result = await sut.Archive(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal(JobStatus.Archived, reloaded.Status);
    }

    [Fact]
    public async Task Archive_AlreadyArchived_IsIdempotent()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Archived);
        var sut = NewController(factory);

        var result = await sut.Archive(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal(JobStatus.Archived, reloaded.Status);
    }

    [Fact]
    public async Task Archive_UnknownId_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var result = await sut.Archive(Guid.NewGuid(), new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Activate
    // ============================================================

    [Fact]
    public async Task Activate_DraftJob_SetsStatusActive()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Draft);
        var sut = NewController(factory);

        var result = await sut.Activate(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal(JobStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task Activate_AlreadyActive_IsIdempotent()
    {
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Active);
        var sut = NewController(factory);

        var result = await sut.Activate(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal(JobStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task Activate_ArchivedJob_ReturnsConflictProblem()
    {
        // Activate refuses Archived→Active so the audit verb stays
        // correct — Restore is the right URL for that path. Issue #25.
        // The 409 carries a problem-detail pointing at /restore.
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Archived);
        var sut = NewController(factory);

        var result = await sut.Activate(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
    }

    [Fact]
    public async Task Restore_ArchivedJob_ReturnsNoContentAndMovesToActive()
    {
        // Happy path for issue #25: Archived → Active via /restore.
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Archived);
        var sut = NewController(factory);

        var result = await sut.Restore(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        using var db = factory.NewActiveContext();
        var reloaded = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == seeded.Id);
        Assert.Equal(JobStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task Restore_ActiveJob_ReturnsConflictProblem()
    {
        // Restore is for Archived → Active only. POSTing /restore
        // against an Active job returns 409 (not a 204 idempotent no-op)
        // so misuse of the verb surfaces instead of being papered over.
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Active);
        var sut = NewController(factory);

        var result = await sut.Restore(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
    }

    [Fact]
    public async Task Restore_DraftJob_ReturnsConflictProblem()
    {
        // Draft jobs are activated via /activate, not /restore.
        var factory = new FakeTenantDbContextFactory();
        var seeded = SeedJob(factory, status: JobStatus.Draft);
        var sut = NewController(factory);

        var result = await sut.Restore(seeded.Id, new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
    }

    [Fact]
    public async Task Restore_UnknownId_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var result = await sut.Restore(Guid.NewGuid(), new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Activate_UnknownId_ReturnsNotFoundProblem()
    {
        var factory = new FakeTenantDbContextFactory();
        var sut = NewController(factory);

        var result = await sut.Activate(Guid.NewGuid(), new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // JobLifecycle contract tests live in Core.Tests/Lifecycle/JobLifecycleTests
    // — the rules are domain, not controller. The controller-level
    // Activate / Archive / Update tests above already prove the
    // controller defers to the lifecycle map.
}
