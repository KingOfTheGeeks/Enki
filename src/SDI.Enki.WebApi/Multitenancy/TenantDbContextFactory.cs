using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Multitenancy;

public sealed class TenantDbContextFactory(IHttpContextAccessor httpContextAccessor) : ITenantDbContextFactory
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

    private static TenantDbContext Build(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new TenantDbContext(options);
    }
}
