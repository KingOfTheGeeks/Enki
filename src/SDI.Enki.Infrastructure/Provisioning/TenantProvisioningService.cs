using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Migrations;
using SDI.Enki.Core.Master.Migrations.Enums;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Internal;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Infrastructure.Provisioning;

public sealed class TenantProvisioningService(
    AthenaMasterDbContext master,
    string masterConnectionString,
    ILogger<TenantProvisioningService> logger) : ITenantProvisioningService
{
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
        var tenant = new Tenant(request.Code, request.Name)
        {
            DisplayName = request.DisplayName,
            Region = request.Region,
            ContactEmail = request.ContactEmail,
            Notes = request.Notes,
        };
        master.Tenants.Add(tenant);

        var activeRow  = new TenantDatabase(tenant.Id, TenantDatabaseKind.Active,  serverInstance, activeDbName);
        var archiveRow = new TenantDatabase(tenant.Id, TenantDatabaseKind.Archive, serverInstance, archiveDbName);
        master.TenantDatabases.AddRange(activeRow, archiveRow);

        await master.SaveChangesAsync(ct);

        try
        {
            // 2. Physical CREATE DATABASE (each in its own non-transactional call).
            await CreateDatabaseAsync(activeDbName, ct);
            await CreateDatabaseAsync(archiveDbName, ct);

            // 3. Apply EF migrations to both, recording an audit row per attempt.
            var appliedVersion = await ApplyTenantMigrationsAsync(tenant.Id, activeRow, ct);
            await ApplyTenantMigrationsAsync(tenant.Id, archiveRow, ct);

            // 4. Flip Archive to READ_ONLY. Active stays READ_WRITE.
            await SetDatabaseReadOnlyAsync(archiveDbName, readOnly: true, ct);

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
            try { await master.SaveChangesAsync(CancellationToken.None); }
            catch { /* best-effort — master state is already behind */ }

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

    private async Task CreateDatabaseAsync(string databaseName, CancellationToken ct)
    {
        // CREATE DATABASE cannot run inside a transaction. Quote the name
        // defensively — DatabaseNaming has already validated it, but belt-and-suspenders.
        var adminConn = TenantConnectionStringBuilder.ForServerAdminConnection(masterConnectionString);
        await using var conn = new SqlConnection(adminConn);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}];";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SetDatabaseReadOnlyAsync(string databaseName, bool readOnly, CancellationToken ct)
    {
        var adminConn = TenantConnectionStringBuilder.ForServerAdminConnection(masterConnectionString);
        await using var conn = new SqlConnection(adminConn);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = readOnly
            ? $"ALTER DATABASE [{databaseName}] SET READ_ONLY WITH ROLLBACK IMMEDIATE;"
            : $"ALTER DATABASE [{databaseName}] SET READ_WRITE WITH ROLLBACK IMMEDIATE;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

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
                .UseSqlServer(tenantConn)
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
