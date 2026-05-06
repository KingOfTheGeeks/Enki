using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using Testcontainers.MsSql;

namespace SDI.Enki.Infrastructure.Tests.SqlServer;

/// <summary>
/// Smoke tests that exercise schema constraints **only enforced by
/// real SQL Server** — filtered UNIQUE indexes. EF InMemory accepts
/// inserts that violate them; the rest of the Infrastructure suite
/// uses InMemory, so without these tests the repo proves the EF
/// model declares the constraints but not that the database itself
/// enforces them. Architecture review item 7.
///
/// <para>
/// Phase 2 note: the previous <c>CK_Shots_ExactlyOneParent</c> CHECK
/// is gone — Shot now has a single <c>RunId</c> parent FK rather than
/// the legacy "exactly one of GradientId / RotaryId / PassiveId" XOR.
/// The two Shot-parent smokes were removed alongside that constraint.
/// </para>
///
/// <para>
/// Runs against a Testcontainers-managed SQL Server 2022 instance.
/// On a Docker-less runner (CI without docker-in-docker, dev box
/// without Docker Desktop running) every test in the class
/// <see cref="SkipException">skips</see> rather than fails — the
/// container probe in the fixture catches the daemon-unavailable
/// failure once and surfaces it via <see cref="DockerAvailable"/>.
/// </para>
///
/// <para>
/// Excluded from the default test run via
/// <c>[Trait("Category", "Sql")]</c> so plain
/// <c>dotnet test</c> stays fast; opt in with
/// <c>--filter Category=Sql</c> when explicitly testing schema
/// enforcement, and CI can opt out with
/// <c>--filter Category!=Sql</c> on runners without Docker.
/// </para>
/// </summary>
[Collection("Sql Server")]
[Trait("Category", "Sql")]
public class SchemaConstraintsSmoke : IClassFixture<SqlServerContainerFixture>
{
    private readonly SqlServerContainerFixture _fx;

    public SchemaConstraintsSmoke(SqlServerContainerFixture fx) => _fx = fx;

    // ---------- Filtered unique index: per-shot Magnetics lookup ----------

    [SkippableFact]
    public async Task DuplicateMagneticsLookup_ViolatesFilteredUnique()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        await using var db = _fx.CreateContext();

        // First lookup row with WellId=null lands fine. Second row with
        // identical (BTotal, Dip, Declination) and WellId=null hits the
        // filtered unique index `(BTotal, Dip, Declination) WHERE
        // WellId IS NULL`. EF InMemory would let both through — SQL
        // Server raises a unique-constraint violation (error 2601 or
        // 2627).
        db.Magnetics.Add(new Magnetics(50_000, 65, 5) { WellId = null });
        await db.SaveChangesAsync();

        db.Magnetics.Add(new Magnetics(50_000, 65, 5) { WellId = null });

        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertSqlUniqueViolation(ex);
    }

    // ---------- Filtered unique index: at most one per-well row ----------

    [SkippableFact]
    public async Task DuplicatePerWellMagnetics_ViolatesFilteredUnique()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        await using var db = _fx.CreateContext();

        var job = new Job("SmokeJob", "Smoke", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var well = new Well(job.Id, "SmokeWell", WellType.Target);
        db.Wells.Add(well);
        await db.SaveChangesAsync();

        // First per-well row lands fine. Second per-well row with the
        // same WellId hits the `WellId WHERE WellId IS NOT NULL`
        // filtered unique index — at most one Magnetics per Well.
        db.Magnetics.Add(new Magnetics(48_000, 60, 4) { WellId = well.Id });
        await db.SaveChangesAsync();

        db.Magnetics.Add(new Magnetics(49_500, 61, 4.5) { WellId = well.Id });

        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        AssertSqlUniqueViolation(ex);
    }

    // ---------- helpers ----------

    /// <summary>
    /// SQL Server unique-constraint violations come back as error
    /// number 2601 (unique index) or 2627 (PK / unique constraint).
    /// We accept either — the specific number depends on whether
    /// the index was created via <c>CREATE UNIQUE INDEX</c> or as
    /// part of a <c>CONSTRAINT</c>.
    /// </summary>
    private static void AssertSqlUniqueViolation(DbUpdateException ex)
    {
        var sqlEx = FindSqlException(ex);
        Assert.NotNull(sqlEx);
        Assert.Contains(sqlEx!.Number, new[] { 2601, 2627 });
    }

    private static SqlException? FindSqlException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is SqlException sqlEx) return sqlEx;
            ex = ex.InnerException;
        }
        return null;
    }
}

/// <summary>
/// Boots a SQL Server container once per test class collection and
/// reuses it across the constraint smokes. <see cref="DockerAvailable"/>
/// is false (and <see cref="SkipReason"/> populated) when Docker
/// isn't reachable — every test then skips rather than fails.
///
/// <para>
/// Each test gets its own freshly-migrated database name so inserts
/// from one test can't pollute another. SQL Server containers are
/// expensive to spin up (~20 s on cold start) so the host stays for
/// the lifetime of the collection.
/// </para>
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool   DockerAvailable { get; private set; }
    public string SkipReason      { get; private set; } = "Docker not probed yet.";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder().Build();
            await _container.StartAsync();
            DockerAvailable = true;
        }
        catch (Exception ex)
        {
            // Most likely failure: Docker daemon isn't running, or the
            // host can't pull the SQL Server 2022 image. Either way,
            // skip every test in this collection rather than red-failing
            // the suite — local dev without Docker should still get a
            // green test run.
            DockerAvailable = false;
            SkipReason      =
                $"SQL Server container could not start: {ex.GetType().Name}: {ex.Message}. " +
                "Start Docker Desktop (or run on a CI agent with Docker available) to exercise " +
                "the schema-constraint smokes.";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>
    /// Build a fresh <see cref="TenantDbContext"/> against a unique
    /// database inside the running SQL Server container. Each test
    /// calls this and gets its own DB so the inserts can't collide
    /// across tests.
    /// </summary>
    public TenantDbContext CreateContext()
    {
        if (_container is null || !DockerAvailable)
            throw new InvalidOperationException(
                "SQL Server container is unavailable; tests should Skip.IfNot(DockerAvailable, ...) first.");

        // Per-test database name. The MsSqlContainer ships with a
        // master DB; create the test DB on demand so each test starts
        // from a clean migrated state.
        var dbName = $"SmokeTest_{Guid.NewGuid():N}";

        var masterCs = _container.GetConnectionString();
        using (var conn = new SqlConnection(masterCs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            cmd.ExecuteNonQuery();
        }

        var testCs = new SqlConnectionStringBuilder(masterCs) { InitialCatalog = dbName }.ToString();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(testCs)
            .Options;

        var ctx = new TenantDbContext(options);
        ctx.Database.Migrate();
        return ctx;
    }

    /// <summary>
    /// Provision a fresh, migrated master database inside the running
    /// SQL Server container and return its connection string. Master-DB
    /// smokes (e.g. <c>ProvisioningRaceSmoke</c>) need to spin multiple
    /// <see cref="EnkiMasterDbContext"/> instances against the same DB
    /// to exercise concurrency paths, so the fixture exposes the
    /// connection string rather than handing back a pre-built context.
    /// </summary>
    public async Task<string> CreateMasterDatabaseAsync(CancellationToken ct = default)
    {
        if (_container is null || !DockerAvailable)
            throw new InvalidOperationException(
                "SQL Server container is unavailable; tests should Skip.IfNot(DockerAvailable, ...) first.");

        var dbName = $"MasterSmoke_{Guid.NewGuid():N}";

        var rootCs = _container.GetConnectionString();
        await using (var conn = new SqlConnection(rootCs))
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var masterCs = new SqlConnectionStringBuilder(rootCs) { InitialCatalog = dbName }.ToString();
        var options = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseSqlServer(masterCs)
            .Options;

        await using var ctx = new EnkiMasterDbContext(options);
        await ctx.Database.MigrateAsync(ct);
        return masterCs;
    }
}

/// <summary>
/// Forces every <see cref="SchemaConstraintsSmoke"/> test to share
/// the single <see cref="SqlServerContainerFixture"/> and run
/// non-parallel — the SQL Server image takes ~20 s to start, so
/// running tests in parallel each spinning their own container would
/// be glacial.
/// </summary>
[CollectionDefinition("Sql Server", DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture> { }
