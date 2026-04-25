using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.Isolation.Tests;

/// <summary>
/// Test double for <see cref="ITenantDbContextFactory"/> that maps the
/// tenant code on <see cref="TenantContext"/> to a tenant-specific
/// EF InMemory store. Lets the isolation tests assert that a request
/// for tenant A literally cannot see rows from tenant B's store —
/// any leak would manifest as the wrong row count returned.
///
/// <para>
/// Per-tenant store names are computed deterministically from the
/// fixture's instance id + the tenant code, so parallel tests don't
/// cross-pollinate.
/// </para>
/// </summary>
public sealed class IsolatingTenantDbContextFactory : ITenantDbContextFactory
{
    private readonly IHttpContextAccessor _http;
    private readonly string _suffix;

    public IsolatingTenantDbContextFactory(IHttpContextAccessor http, string suffix)
    {
        _http   = http;
        _suffix = suffix;
    }

    public TenantDbContext CreateActive()  => Build("active");
    public TenantDbContext CreateArchive() => Build("archive");

    /// <summary>
    /// Test-side accessor — gives the test a context against the same
    /// store that <see cref="CreateActive"/> would resolve to for the
    /// given <paramref name="tenantCode"/>. Used to seed before the
    /// HTTP request fires.
    /// </summary>
    public TenantDbContext OpenActiveFor(string tenantCode) =>
        BuildForCode(tenantCode, "active");

    private TenantDbContext Build(string kind)
    {
        var ctx = _http.HttpContext
            ?? throw new InvalidOperationException("No HttpContext on this thread.");

        if (ctx.Items[TenantContext.ItemKey] is not TenantContext tenant)
            throw new InvalidOperationException(
                "No TenantContext on the request — TenantRoutingMiddleware should have populated it.");

        return BuildForCode(tenant.Code, kind);
    }

    private TenantDbContext BuildForCode(string code, string kind)
    {
        var name = $"isolation-{_suffix}-{code}-{kind}";
        var opts = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new TenantDbContext(opts);
    }
}
