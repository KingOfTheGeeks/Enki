using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Jobs;

namespace SDI.Enki.Isolation.Tests;

/// <summary>
/// Highest-stakes contract in the codebase: a request scoped to tenant A
/// must not, under any circumstance, return data from tenant B. The
/// mechanism (TenantRoutingMiddleware → TenantContext.Items →
/// ITenantDbContextFactory.CreateActive) is small, but the consequences
/// of a regression are catastrophic — one wrong factory call would leak
/// every tenant's drilling data to whoever asked.
///
/// <para>
/// These tests stand up two tenants with distinct sets of jobs and
/// assert that each tenant's <c>/jobs</c> endpoint sees only its own
/// rows. Adding a Wells endpoint or a Runs endpoint? Add a parallel
/// test here at the same time.
/// </para>
/// </summary>
public class TenantDataIsolationTests : IClassFixture<IsolationTestFactory>
{
    private readonly IsolationTestFactory _factory;

    public TenantDataIsolationTests(IsolationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TenantA_JobsEndpoint_ReturnsOnlyTenantAJobs()
    {
        // Force the host to spin up so the IsolatingTenantDbContextFactory
        // has been constructed and we can seed via it.
        var client = _factory.CreateClient();
        await SeedAsync();

        // Tenant ALPHA's endpoint
        var responseA = await client.GetAsync("/tenants/ALPHA/jobs");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var jobsA = await responseA.Content.ReadFromJsonAsync<List<JobSummaryDto>>();
        Assert.NotNull(jobsA);

        // Should ONLY see ALPHA's jobs.
        Assert.Equal(2, jobsA!.Count);
        Assert.All(jobsA, j => Assert.StartsWith("ALPHA-", j.Name));
        Assert.DoesNotContain(jobsA, j => j.Name.StartsWith("BRAVO-"));
    }

    [Fact]
    public async Task TenantB_JobsEndpoint_ReturnsOnlyTenantBJobs()
    {
        var client = _factory.CreateClient();
        await SeedAsync();

        var responseB = await client.GetAsync("/tenants/BRAVO/jobs");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var jobsB = await responseB.Content.ReadFromJsonAsync<List<JobSummaryDto>>();
        Assert.NotNull(jobsB);

        Assert.Single(jobsB!);
        Assert.StartsWith("BRAVO-", jobsB![0].Name);
        Assert.DoesNotContain(jobsB, j => j.Name.StartsWith("ALPHA-"));
    }

    [Fact]
    public async Task UnknownTenantCode_Returns404_AsProblemDetails()
    {
        var client = _factory.CreateClient();
        await SeedAsync();

        var response = await client.GetAsync("/tenants/UNKNOWN/jobs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem!.Status);
        Assert.Equal("Tenant not found", problem.Title);
        Assert.Contains("UNKNOWN", problem.Detail);
    }

    [Fact]
    public async Task InactiveTenant_Returns404_EvenForEnkiAdmin()
    {
        // CHARLIE is seeded with Status=Inactive. The test client carries
        // role=enki-admin (see IsolationAuthHandler) — enki-admin must
        // still 404 on tenant-scoped routes. To work in an Inactive
        // tenant, an admin reactivates via the master endpoint first.
        var client = _factory.CreateClient();
        await SeedAsync();

        var response = await client.GetAsync("/tenants/CHARLIE/jobs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem!.Status);
        Assert.Contains("CHARLIE", problem.Detail);
    }

    [Fact]
    public async Task ArchivedTenant_Returns404()
    {
        var client = _factory.CreateClient();
        await SeedAsync();

        var response = await client.GetAsync("/tenants/DELTA/jobs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Sets up:
    /// <list type="bullet">
    ///   <item>Two Active tenants in master (ALPHA, BRAVO) with both
    ///   Active + Archive TenantDatabase rows so middleware resolution
    ///   succeeds.</item>
    ///   <item>One Inactive tenant (CHARLIE) and one Archived (DELTA),
    ///   each with full TenantDatabase rows so any 404 has to come from
    ///   the status check, not a missing-DB fall-through.</item>
    ///   <item>Two jobs in ALPHA's tenant store; one job in BRAVO's.</item>
    /// </list>
    /// Idempotent — uses <c>OrUpdate</c> patterns so multiple test
    /// methods seeding into the same fixture don't collide.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var master = scope.ServiceProvider.GetRequiredService<EnkiMasterDbContext>();
        await master.Database.EnsureCreatedAsync();

        if (!await master.Tenants.AnyAsync(t => t.Code == "ALPHA"))
        {
            var alpha = new Tenant("ALPHA", "Alpha Corp") { Status = TenantStatus.Active };
            master.Tenants.Add(alpha);
            master.TenantDatabases.Add(new TenantDatabase(
                alpha.Id, TenantDatabaseKind.Active, "test-server", "Enki_ALPHA_Active"));
            master.TenantDatabases.Add(new TenantDatabase(
                alpha.Id, TenantDatabaseKind.Archive, "test-server", "Enki_ALPHA_Archive"));
        }
        if (!await master.Tenants.AnyAsync(t => t.Code == "BRAVO"))
        {
            var bravo = new Tenant("BRAVO", "Bravo Corp") { Status = TenantStatus.Active };
            master.Tenants.Add(bravo);
            master.TenantDatabases.Add(new TenantDatabase(
                bravo.Id, TenantDatabaseKind.Active, "test-server", "Enki_BRAVO_Active"));
            master.TenantDatabases.Add(new TenantDatabase(
                bravo.Id, TenantDatabaseKind.Archive, "test-server", "Enki_BRAVO_Archive"));
        }
        if (!await master.Tenants.AnyAsync(t => t.Code == "CHARLIE"))
        {
            var charlie = new Tenant("CHARLIE", "Charlie Corp") { Status = TenantStatus.Inactive };
            master.Tenants.Add(charlie);
            master.TenantDatabases.Add(new TenantDatabase(
                charlie.Id, TenantDatabaseKind.Active, "test-server", "Enki_CHARLIE_Active"));
            master.TenantDatabases.Add(new TenantDatabase(
                charlie.Id, TenantDatabaseKind.Archive, "test-server", "Enki_CHARLIE_Archive"));
        }
        if (!await master.Tenants.AnyAsync(t => t.Code == "DELTA"))
        {
            var delta = new Tenant("DELTA", "Delta Corp") { Status = TenantStatus.Archived };
            master.Tenants.Add(delta);
            master.TenantDatabases.Add(new TenantDatabase(
                delta.Id, TenantDatabaseKind.Active, "test-server", "Enki_DELTA_Active"));
            master.TenantDatabases.Add(new TenantDatabase(
                delta.Id, TenantDatabaseKind.Archive, "test-server", "Enki_DELTA_Archive"));
        }
        await master.SaveChangesAsync();

        await using (var alphaDb = _factory.OpenTenantStore("ALPHA"))
        {
            await alphaDb.Database.EnsureCreatedAsync();
            if (!await alphaDb.Jobs.AnyAsync())
            {
                alphaDb.Jobs.AddRange(
                    new Job("ALPHA-Job-1", "Alpha first",  UnitSystem.Field) { Status = JobStatus.Active },
                    new Job("ALPHA-Job-2", "Alpha second", UnitSystem.Field) { Status = JobStatus.Draft });
                await alphaDb.SaveChangesAsync();
            }
        }

        await using (var bravoDb = _factory.OpenTenantStore("BRAVO"))
        {
            await bravoDb.Database.EnsureCreatedAsync();
            if (!await bravoDb.Jobs.AnyAsync())
            {
                bravoDb.Jobs.Add(
                    new Job("BRAVO-Job-1", "Bravo only", UnitSystem.Metric) { Status = JobStatus.Active });
                await bravoDb.SaveChangesAsync();
            }
        }
    }
}
