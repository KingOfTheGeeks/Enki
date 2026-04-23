namespace SDI.Enki.WebApi.Multitenancy;

/// <summary>
/// Per-request tenant scope populated by <see cref="TenantRoutingMiddleware"/>
/// and read by <see cref="TenantDbContextFactory"/>. Cached in
/// <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> under the key
/// <see cref="ItemKey"/>.
/// </summary>
public sealed record TenantContext(
    Guid TenantId,
    string Code,
    string ActiveConnectionString,
    string ArchiveConnectionString)
{
    public const string ItemKey = "EnkiTenantContext";
}
