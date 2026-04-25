using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Multitenancy;

/// <summary>
/// Per-request <see cref="TenantDbContext"/> builder. Pulls the
/// connection string from the request-scoped <see cref="TenantContext"/>
/// (populated by <c>TenantRoutingMiddleware</c>) and threads
/// <see cref="ICurrentUser"/> through so the audit interceptor on
/// <c>TenantDbContext.SaveChangesAsync</c> can stamp the right actor.
/// </summary>
public sealed class TenantDbContextFactory(
    IHttpContextAccessor httpContextAccessor,
    ICurrentUser currentUser) : ITenantDbContextFactory
{
    public TenantDbContext CreateActive() =>
        Build(RequireContext().ActiveConnectionString);

    public TenantDbContext CreateArchive() =>
        Build(RequireContext().ArchiveConnectionString);

    private TenantContext RequireContext()
    {
        var http = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. TenantDbContextFactory can only be used inside a request scope.");

        if (http.Items[TenantContext.ItemKey] is not TenantContext tenant)
            throw new InvalidOperationException(
                "No tenant context on this request. Ensure the endpoint is routed under " +
                "'/tenants/{tenantCode}/...' so TenantRoutingMiddleware populates it.");

        return tenant;
    }

    private TenantDbContext Build(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                // Same retry policy as the master + identity contexts.
                // The acute case is the 4060 race right after tenant
                // provisioning (DB exists in metadata but not yet
                // openable); the broader case is generic transient
                // SQL Server faults on a busy or warming up server.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 6,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            })
            .Options;
        return new TenantDbContext(options, currentUser);
    }
}
