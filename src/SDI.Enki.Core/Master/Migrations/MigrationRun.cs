using SDI.Enki.Core.Master.Migrations.Enums;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;

namespace SDI.Enki.Core.Master.Migrations;

/// <summary>
/// Audit row written by the SDI.Enki.Migrator console app each time it applies
/// EF migrations to a tenant database. Deployment pipelines block until every
/// (TenantId, Kind) row reports <see cref="MigrationRunStatus.Success"/> for the
/// target schema version.
/// </summary>
public class MigrationRun(Guid tenantId, TenantDatabaseKind kind, string targetVersion)
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; } = tenantId;

    public TenantDatabaseKind Kind { get; set; } = kind;

    /// <summary>EF migration id being applied, e.g. "20260423_Initial".</summary>
    public string TargetVersion { get; set; } = targetVersion;

    public MigrationRunStatus Status { get; set; } = MigrationRunStatus.Running;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Error text when Status is Failed; null otherwise.</summary>
    public string? Error { get; set; }

    // EF nav — deliberately weak (no Tenant nav) because MigrationRun outlives tenants
}
