using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.WebApi.Background;

namespace SDI.Enki.WebApi.Tests.Background;

/// <summary>
/// Coverage for the master-roster + skip-on-zero-days path of
/// <see cref="TenantAuditRetentionService"/>. The per-tenant prune
/// itself uses EF's <c>ExecuteDeleteAsync</c> which the InMemory
/// provider doesn't support, so the actual delete is verified via
/// the existing <c>SchemaConstraintsSmoke</c> Testcontainers test on
/// the SQL-Server side; here we assert the orchestration shape.
/// </summary>
public class TenantAuditRetentionServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"tenant-retention-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    /// <summary>
    /// Build a service-scope factory that returns the supplied
    /// <see cref="EnkiMasterDbContext"/> + a real
    /// <see cref="DatabaseAdmin"/> bound to a placeholder
    /// <see cref="ProvisioningOptions"/>. Connection-string building
    /// just needs the option present; no SQL connection happens
    /// here.
    /// </summary>
    private static IServiceScopeFactory NewScopeFactory(EnkiMasterDbContext masterDb)
    {
        var services = new ServiceCollection();
        services.AddLogging();   // DatabaseAdmin asks for an ILogger<>.
        services.AddSingleton(masterDb);
        services.AddSingleton(new ProvisioningOptions(
            MasterConnectionString: "Server=localhost;Database=Enki_Master;Trusted_Connection=True;",
            SeedSampleData:         false));
        services.AddScoped<DatabaseAdmin>();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    private static TenantAuditRetentionService NewService(
        EnkiMasterDbContext masterDb, AuditRetentionOptions opt)
    {
        return new TenantAuditRetentionService(
            scopeFactory: NewScopeFactory(masterDb),
            options:      Options.Create(opt),
            logger:       NullLogger<TenantAuditRetentionService>.Instance,
            timeProvider: new FixedTimeProvider(FixedNow));
    }

    [Fact]
    public async Task PruneOnceAsync_ZeroOrNegativeDays_NoOps()
    {
        await using var db = NewDb();
        var sut = NewService(db, new AuditRetentionOptions { TenantAuditLogDays = 0 });

        // Should complete cleanly, no exceptions, logs the skip.
        await sut.PruneOnceAsync(
            new AuditRetentionOptions { TenantAuditLogDays = 0 },
            CancellationToken.None);
    }

    [Fact]
    public async Task PruneOnceAsync_NoActiveTenants_CompletesWithoutError()
    {
        await using var db = NewDb();
        var sut = NewService(db, new AuditRetentionOptions { TenantAuditLogDays = 730 });

        // Empty master roster — service walks an empty list and logs
        // 0 swept / 0 failed. No throws.
        await sut.PruneOnceAsync(
            new AuditRetentionOptions { TenantAuditLogDays = 730 },
            CancellationToken.None);
    }

    [Fact]
    public async Task PruneOnceAsync_InactiveTenantsExcluded()
    {
        await using var db = NewDb();
        var inactive = new Tenant("ARCHIVED", "Old Corp")
        {
            Status = TenantStatus.Inactive,
        };
        db.Tenants.Add(inactive);
        await db.SaveChangesAsync();

        var sut = NewService(db, new AuditRetentionOptions { TenantAuditLogDays = 730 });

        // Inactive tenants are filtered out at the master query; the
        // sweep effectively no-ops. Verifying via "no exceptions" because
        // the service never tries to connect to ARCHIVED's tenant DB.
        await sut.PruneOnceAsync(
            new AuditRetentionOptions { TenantAuditLogDays = 730 },
            CancellationToken.None);
    }

    [Fact]
    public async Task PruneOnceAsync_TenantWithoutActiveDatabaseRow_FilteredOut()
    {
        await using var db = NewDb();
        // Tenant marked Active but missing the Active TenantDatabase row
        // — filtered out by the inner Where(x => x.DatabaseName != null).
        // Sweep proceeds without trying to connect anywhere.
        db.Tenants.Add(new Tenant("ORPHAN", "Orphan Corp")
        {
            Status = TenantStatus.Active,
        });
        await db.SaveChangesAsync();

        var sut = NewService(db, new AuditRetentionOptions { TenantAuditLogDays = 730 });

        await sut.PruneOnceAsync(
            new AuditRetentionOptions { TenantAuditLogDays = 730 },
            CancellationToken.None);
    }

}
