using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;

namespace SDI.Enki.WebApi.Multitenancy;

/// <summary>
/// Runs after ASP.NET Core routing. When a route binds a <c>tenantCode</c>
/// parameter (e.g. <c>/tenants/{tenantCode}/jobs</c>), resolves it to a
/// <see cref="TenantContext"/> against the master registry and stores it on
/// the request. Returns 404 if the code doesn't match a known tenant.
/// Master-level endpoints (/tenants with no code) pass through untouched.
/// </summary>
public sealed class TenantRoutingMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Stable cache-key formatter shared with controllers that need to
    /// bust the entry on tenant-status changes (deactivate / reactivate).
    /// Without an out-of-band invalidation path, the 5-minute TTL means
    /// a deactivated tenant continues to serve up to 5 minutes after
    /// the status flip — unacceptable for revocation.
    /// </summary>
    public static string CacheKeyFor(string tenantCode) => $"enki.tenant.{tenantCode}";

    public async Task InvokeAsync(
        HttpContext ctx,
        AthenaMasterDbContext master,
        DatabaseAdmin databaseAdmin,
        IMemoryCache cache,
        ILogger<TenantRoutingMiddleware> logger)
    {
        if (ctx.Request.RouteValues["tenantCode"] is not string code || string.IsNullOrWhiteSpace(code))
        {
            await next(ctx);
            return;
        }

        var cacheKey = CacheKeyFor(code);
        var tenantContext = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var tenant = await master.Tenants
                .Include(t => t.Databases)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Code == code);

            if (tenant is null)
                return null;

            var active  = tenant.Databases.FirstOrDefault(d => d.Kind == TenantDatabaseKind.Active);
            var archive = tenant.Databases.FirstOrDefault(d => d.Kind == TenantDatabaseKind.Archive);
            if (active is null || archive is null)
            {
                logger.LogError("Tenant {Code} is missing an Active or Archive database row.", code);
                return null;
            }

            return new TenantContext(
                TenantId:                  tenant.Id,
                Code:                      tenant.Code,
                ActiveConnectionString:    databaseAdmin.BuildTenantConnectionString(active.DatabaseName),
                ArchiveConnectionString:   databaseAdmin.BuildTenantConnectionString(archive.DatabaseName));
        });

        if (tenantContext is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { error = $"Tenant '{code}' not found." });
            return;
        }

        ctx.Items[TenantContext.ItemKey] = tenantContext;
        await next(ctx);
    }
}
