namespace SDI.Enki.WebApi.Multitenancy;

/// <summary>
/// Marks a controller (or single action) as exempt from
/// <see cref="TenantRoutingMiddleware"/>.
///
/// <para>
/// The middleware has two responsibilities — resolve a per-request
/// <see cref="TenantContext"/> against the master registry, and
/// hard-revoke (404) requests for non-Active tenants. Both are correct
/// for tenant-scoped routes (<c>/tenants/{code}/jobs</c>,
/// <c>/tenants/{code}/runs</c>, …) but wrong for the master-registry
/// routes on <see cref="Controllers.TenantsController"/>: those
/// endpoints carry <c>{tenantCode}</c> in the route purely as an
/// addressing key, but they:
/// </para>
///
/// <list type="bullet">
///   <item>operate on the master DB directly (no <c>TenantContext</c> needed), and</item>
///   <item>must remain reachable for non-Active tenants — admins still
///   need to view, edit, and reactivate Inactive tenants. Without this
///   opt-out the reactivate endpoint becomes unreachable, since its
///   precondition (<c>Status=Inactive</c>) is exactly what the middleware
///   uses to 404 the request before the controller runs.</item>
/// </list>
///
/// <para>
/// Tag the controller (or a specific action) with <c>[SkipTenantRouting]</c>
/// and the middleware passes the request through unchanged. Authorization
/// still runs — the policies (<c>EnkiApiScope</c>, <c>CanAccessTenant</c>,
/// <c>EnkiAdminOnly</c>) read the <c>tenantCode</c> route value directly
/// and don't depend on the middleware-set context.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SkipTenantRoutingAttribute : Attribute { }
