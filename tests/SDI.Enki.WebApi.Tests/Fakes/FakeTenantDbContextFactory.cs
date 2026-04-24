using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Tests.Fakes;

/// <summary>
/// Test double for <see cref="ITenantDbContextFactory"/>. Each call to
/// <see cref="CreateActive"/> returns a fresh <see cref="TenantDbContext"/>
/// bound to the same InMemory database — mirrors production semantics
/// where every request gets its own context but they all point at the
/// same underlying database.
///
/// The Archive context points at a separate InMemory database so tests
/// that want to verify Active/Archive independence can do so without
/// cross-talk. Tests that don't care about the archive just ignore it.
/// </summary>
internal sealed class FakeTenantDbContextFactory : ITenantDbContextFactory
{
    private readonly string _activeDbName;
    private readonly string _archiveDbName;

    public FakeTenantDbContextFactory()
    {
        var suffix = Guid.NewGuid().ToString("N");
        _activeDbName  = $"jobs-active-{suffix}";
        _archiveDbName = $"jobs-archive-{suffix}";
    }

    public TenantDbContext CreateActive()  => Build(_activeDbName);
    public TenantDbContext CreateArchive() => Build(_archiveDbName);

    /// <summary>Hand-out for arrange/assert: a context bound to the same
    /// Active store that <see cref="CreateActive"/> returns. Tests own its
    /// disposal.</summary>
    public TenantDbContext NewActiveContext() => Build(_activeDbName);

    private static TenantDbContext Build(string dbName)
    {
        var opts = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new TenantDbContext(opts);
    }
}
