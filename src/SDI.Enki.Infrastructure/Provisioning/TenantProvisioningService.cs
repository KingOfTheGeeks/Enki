using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Migrations;
using SDI.Enki.Core.Master.Migrations.Enums;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Internal;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Infrastructure.Surveys;

namespace SDI.Enki.Infrastructure.Provisioning;

public sealed class TenantProvisioningService(
    EnkiMasterDbContext master,
    ProvisioningOptions options,
    DatabaseAdmin databaseAdmin,
    ISurveyAutoCalculator surveyAutoCalculator,
    ILogger<TenantProvisioningService> logger) : ITenantProvisioningService
{
    private readonly string masterConnectionString = options.MasterConnectionString;


    public async Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken ct = default)
    {
        DatabaseNaming.ValidateCode(request.Code);

        await EnsureCodeIsUniqueAsync(request.Code, ct);

        var serverInstance = request.ServerInstanceOverride
            ?? TenantConnectionStringBuilder.GetServerInstance(masterConnectionString);
        var activeDbName = DatabaseNaming.ForKind(request.Code, TenantDatabaseKind.Active);
        var archiveDbName = DatabaseNaming.ForKind(request.Code, TenantDatabaseKind.Archive);

        // 1. Persist the master-DB rows up front (status = Provisioning).
        //    If anything downstream fails we surface the TenantId so admins
        //    can reconcile / retry without losing state.
        //
        //    Tenant.Id defaults to Guid.NewGuid() at construction; the
        //    DevMasterSeeder overrides it with a SeedTenants-pinned ID
        //    so SeedUsers can bind Tenant-type users by GUID without a
        //    cross-host lookup. Production provisioning leaves it null
        //    so each new tenant gets a fresh ID.
        var tenant = new Tenant(request.Code, request.Name)
        {
            DisplayName  = request.DisplayName,
            ContactEmail = request.ContactEmail,
            Notes        = request.Notes,
        };
        if (request.TenantId is { } pinnedId && pinnedId != Guid.Empty)
        {
            tenant.Id = pinnedId;
        }
        master.Tenants.Add(tenant);

        var activeRow  = new TenantDatabase(tenant.Id, TenantDatabaseKind.Active,  serverInstance, activeDbName);
        var archiveRow = new TenantDatabase(tenant.Id, TenantDatabaseKind.Archive, serverInstance, archiveDbName);
        master.TenantDatabases.AddRange(activeRow, archiveRow);

        try
        {
            await master.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.GetBaseException() is SqlException sqlEx &&
            (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            // Race window: two callers passed EnsureCodeIsUniqueAsync
            // before either committed, and SQL Server's IX_Tenants_Code
            // unique index rejected the loser. Translate to the same
            // friendly TenantProvisioningException the pre-check raises
            // so callers see "Tenant code 'X' already exists." (HTTP
            // 400) instead of a generic 500.
            throw new TenantProvisioningException(
                $"Tenant code '{request.Code}' already exists.",
                inner: ex);
        }

        try
        {
            // 2. Physical CREATE DATABASE (each in its own non-transactional call).
            await databaseAdmin.CreateDatabaseIfMissingAsync(activeDbName, ct);
            await databaseAdmin.CreateDatabaseIfMissingAsync(archiveDbName, ct);

            // 3. Apply EF migrations to both, recording an audit row per attempt.
            var appliedVersion = await ApplyTenantMigrationsAsync(tenant.Id, activeRow, ct);
            await ApplyTenantMigrationsAsync(tenant.Id, archiveRow, ct);

            // 3a. Warmup probe: open a connection + execute SELECT 1
            //     against the freshly-provisioned Active DB. SQL Server
            //     occasionally has a brief window after CREATE DATABASE
            //     where logins with USE-the-DB rights fail with error
            //     4060 ("cannot open database"). The retry policy on the
            //     warmup absorbs that race here, so the next request the
            //     caller makes lands on a fully-attached DB instead of
            //     burning all six retries against a half-attached one.
            await WarmupTenantAsync(activeRow, ct);

            // 3b. Dev-only sample data — populate the Active DB with a
            //     few demo Jobs so the UI has content out of the gate.
            //     Per-request flag so only DevMasterSeeder's curated
            //     demo tenants (PERMIAN / BAKKEN / NORTHSEA / CARNARVON)
            //     get seeded; user-driven provisions from the UI leave
            //     SeedSampleData at the default false.
            if (request.SeedSampleData)
            {
                if (request.SeedSpec is null)
                    throw new TenantProvisioningException(
                        $"Provision request for '{request.Code}' has SeedSampleData=true " +
                        "but no SeedSpec — DevTenantSeeder needs the spec to decide what " +
                        "names / coordinates to seed.");

                await SeedSampleDataAsync(activeRow, request.SeedSpec, ct);
                logger.LogInformation(
                    "Seeded sample data into tenant {Code} Active DB ({DbName})",
                    request.Code, activeRow.DatabaseName);
            }

            // 4. Flip Archive to READ_ONLY. Active stays READ_WRITE.
            await databaseAdmin.SetReadOnlyAsync(archiveDbName, ct);

            // 5. Mark both rows Active; Archive remains logically "Active-in-use"
            //    — the TenantDatabaseStatus enum's "Archived" value is reserved
            //    for decommissioned databases (e.g., when a tenant is deactivated).
            activeRow.Status  = TenantDatabaseStatus.Active;
            archiveRow.Status = TenantDatabaseStatus.Active;
            activeRow.SchemaVersion  = appliedVersion;
            archiveRow.SchemaVersion = appliedVersion;
            activeRow.LastMigrationAt  = DateTimeOffset.UtcNow;
            archiveRow.LastMigrationAt = DateTimeOffset.UtcNow;
            await master.SaveChangesAsync(ct);

            logger.LogInformation(
                "Provisioned tenant {Code} ({TenantId}) with schema version {Version}",
                request.Code, tenant.Id, appliedVersion);

            return new ProvisionTenantResult(
                TenantId: tenant.Id,
                Code: request.Code,
                ServerInstance: serverInstance,
                ActiveDatabaseName: activeDbName,
                ArchiveDatabaseName: archiveDbName,
                AppliedSchemaVersion: appliedVersion,
                CompletedAt: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Provisioning failed for tenant {Code} ({TenantId}); rows left at Status=Failed for cleanup",
                request.Code, tenant.Id);

            activeRow.Status  = TenantDatabaseStatus.Failed;
            archiveRow.Status = TenantDatabaseStatus.Failed;
            try
            {
                await master.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                // Best-effort — primary failure is already logged + about
                // to be rethrown. Log the secondary so a future operator
                // sees that master state didn't catch up to reality.
                logger.LogWarning(saveEx,
                    "Best-effort persist of Status=Failed on tenant {Code} ({TenantId}) " +
                    "did not succeed; master and reality may be out of sync.",
                    request.Code, tenant.Id);
            }

            throw new TenantProvisioningException(
                $"Failed to provision tenant '{request.Code}': {ex.Message}",
                partialTenantId: tenant.Id,
                inner: ex);
        }
    }

    private async Task EnsureCodeIsUniqueAsync(string code, CancellationToken ct)
    {
        var exists = await master.Tenants.AnyAsync(t => t.Code == code, ct);
        if (exists)
            throw new TenantProvisioningException($"Tenant code '{code}' already exists.");
    }

    private async Task SeedSampleDataAsync(TenantDatabase activeRow, TenantSeedSpec spec, CancellationToken ct)
    {
        var tenantConn = Internal.TenantConnectionStringBuilder.ForTenantDatabase(
            masterConnectionString, activeRow.DatabaseName);

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(tenantConn, BuildSqlOptions)
            .Options;

        await using var tenantDb = new TenantDbContext(options);
        // The seeder writes observed (depth/inc/azi) values and zeros
        // for the computed columns; the auto-calc passed here fills in
        // the trajectory for every well right after the rows land, so
        // the very first GET /surveys after provisioning returns
        // already-calculated data. The spec drives per-tenant naming
        // (Permian / Bakken / North Sea) while the trajectory math
        // stays constant across every demo tenant.
        //
        // Master is also passed: the seeder pulls active tools +
        // their latest non-superseded calibrations to assign to seeded
        // Runs (matches the production tool-assignment + snapshot
        // flow so seeded tenants come up with shot creation already
        // unblocked).
        await DevTenantSeeder.SeedAsync(tenantDb, master, surveyAutoCalculator, spec, ct);
    }

    /// <summary>
    /// Round-trips a trivial query against the freshly-provisioned tenant
    /// DB to confirm it's openable before <see cref="ProvisionAsync"/>
    /// returns. The retry policy absorbs the 4060 race that fires when
    /// SQL Server's metadata sync trails the CREATE DATABASE; without
    /// this probe the first real request through TenantDbContextFactory
    /// burns all six retries (~60 s) on a transient fault and surfaces
    /// as <c>RetryLimitExceededException</c>.
    /// </summary>
    private async Task WarmupTenantAsync(TenantDatabase activeRow, CancellationToken ct)
    {
        var tenantConn = Internal.TenantConnectionStringBuilder.ForTenantDatabase(
            masterConnectionString, activeRow.DatabaseName);

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(tenantConn, BuildSqlOptions)
            .Options;

        await using var tenantDb = new TenantDbContext(options);
        await tenantDb.Database.ExecuteSqlRawAsync("SELECT 1", ct);
    }

    /// <summary>
    /// Common SQL Server options used by every per-request tenant context
    /// build inside this service. Retry policy specifically catches the
    /// 4060 ("cannot open database") race that fires when we connect to
    /// a freshly-created tenant DB before SQL Server has finished
    /// attaching it — six attempts × up to 10s backoff is enough for
    /// any real-world warmup.
    /// </summary>
    private static void BuildSqlOptions(SqlServerDbContextOptionsBuilder sql) =>
        sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);

    private async Task<string> ApplyTenantMigrationsAsync(
        Guid tenantId,
        TenantDatabase dbRow,
        CancellationToken ct)
    {
        var targetVersion = typeof(TenantDbContext).Assembly
            .GetType("SDI.Enki.Infrastructure.Migrations.Tenant.TenantDbContextModelSnapshot") is not null
            ? "latest"
            : "unknown";

        var run = new MigrationRun(tenantId, dbRow.Kind, targetVersion);
        master.MigrationRuns.Add(run);
        await master.SaveChangesAsync(ct);

        try
        {
            var tenantConn = TenantConnectionStringBuilder.ForTenantDatabase(
                masterConnectionString, dbRow.DatabaseName);

            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(tenantConn, BuildSqlOptions)
                .Options;

            await using var tenantDb = new TenantDbContext(options);
            await tenantDb.Database.MigrateAsync(ct);

            // Resolve the concrete applied migration id for the audit row.
            var applied = (await tenantDb.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? targetVersion;

            run.Status = MigrationRunStatus.Success;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.TargetVersion = applied;
            await master.SaveChangesAsync(ct);

            return applied;
        }
        catch (Exception ex)
        {
            run.Status = MigrationRunStatus.Failed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Error = ex.Message;
            try { await master.SaveChangesAsync(CancellationToken.None); } catch { /* best-effort */ }
            throw;
        }
    }
}
