using SDI.Enki.Core.Master.Tenants.Enums;

namespace SDI.Enki.Core.Master.Tenants;

/// <summary>
/// Physical database belonging to a tenant. Each tenant has two:
/// an Active database (read-write, hourly backups) and an Archive
/// database (read-only, single FULL at archive-move time).
/// </summary>
public class TenantDatabase(Guid tenantId, TenantDatabaseKind kind, string serverInstance, string databaseName)
{
    /// <summary>FK to <see cref="Tenant.Id"/>. Composite PK (TenantId, Kind).</summary>
    public Guid TenantId { get; set; } = tenantId;

    /// <summary>Active or Archive. Composite PK (TenantId, Kind).</summary>
    public TenantDatabaseKind Kind { get; set; } = kind;

    /// <summary>SQL Server instance — e.g. "enki-sql-prod\\SDI" or "localhost".</summary>
    public string ServerInstance { get; set; } = serverInstance;

    /// <summary>Database name — e.g. "Enki_EXXON_Active".</summary>
    public string DatabaseName { get; set; } = databaseName;

    public TenantDatabaseStatus Status { get; set; } = TenantDatabaseStatus.Provisioning;

    /// <summary>Last applied EF migration id, set by the migrator app.</summary>
    public string? SchemaVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastMigrationAt { get; set; }
    public DateTimeOffset? LastBackupAt { get; set; }

    /// <summary>
    /// EF Core concurrency token. Two provisioning workers writing
    /// <see cref="Status"/> + <see cref="SchemaVersion"/> against the
    /// same TenantDatabase row (e.g. parallel Migrator runs against the
    /// same tenant) would otherwise silently last-writer-wins; with
    /// RowVersion the second SaveChanges throws
    /// <c>DbUpdateConcurrencyException</c> and the global handler maps
    /// it to a 409. Doesn't implement <see cref="IAuditable"/> because
    /// the lifecycle here is provisioning-owned, not user-edited.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    // EF nav
    public Tenant? Tenant { get; set; }
}
