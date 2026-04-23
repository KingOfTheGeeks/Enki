namespace SDI.Enki.WebApi.Multitenancy;

public static class MultitenancyExtensions
{
    /// <summary>
    /// Registers HttpContextAccessor, IMemoryCache, and ITenantDbContextFactory.
    /// Call once in Program.cs alongside AddEnkiInfrastructure.
    /// </summary>
    public static IServiceCollection AddEnkiMultitenancy(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
        return services;
    }

    /// <summary>
    /// Inserts <see cref="TenantRoutingMiddleware"/> into the pipeline. Must
    /// come AFTER <c>UseRouting</c> so that route values are populated.
    /// </summary>
    public static IApplicationBuilder UseTenantRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantRoutingMiddleware>();
}
