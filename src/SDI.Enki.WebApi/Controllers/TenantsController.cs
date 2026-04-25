using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-level tenant registry endpoints. Not scoped to any one tenant —
/// these operate on the master DB directly. Currently requires any caller
/// with an enki-scope bearer token; admin-only gating (policy "EnkiAdmin"
/// checking TenantUser.Role == Admin) comes in a follow-up pass.
///
/// Error surface: expected failures (unknown code → 404; bad state
/// transition → 409) return ProblemDetails via <see cref="EnkiResults"/>
/// extension methods — cleaner than throwing for known outcomes and
/// doesn't trigger the VS debugger's user-unhandled break on every
/// not-found. Truly unexpected exceptions (DbUpdateConcurrencyException,
/// crashes in deeper layers, <see cref="TenantProvisioningException"/>
/// from Infrastructure) still propagate to <c>EnkiExceptionHandler</c>
/// which produces the same ProblemDetails shape.
/// </summary>
[ApiController]
[Route("tenants")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
public sealed class TenantsController(
    EnkiMasterDbContext master,
    ITenantProvisioningService provisioning,
    IMemoryCache cache) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    public async Task<IEnumerable<TenantSummaryDto>> List(CancellationToken ct) =>
        await master.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new TenantSummaryDto(
                t.Id, t.Code, t.Name, t.DisplayName,
                t.Status.Name, t.CreatedAt))
            .ToListAsync(ct);

    // ---------- detail ----------

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct)
    {
        var tenant = await master.Tenants
            .Include(t => t.Databases)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == code, ct);

        if (tenant is null) return this.NotFoundProblem("Tenant", code);

        var active  = tenant.Databases.FirstOrDefault(d => d.Kind == TenantDatabaseKind.Active);
        var archive = tenant.Databases.FirstOrDefault(d => d.Kind == TenantDatabaseKind.Archive);

        return Ok(new TenantDetailDto(
            tenant.Id, tenant.Code, tenant.Name, tenant.DisplayName,
            tenant.Status.Name, tenant.ContactEmail, tenant.Notes,
            tenant.CreatedAt, tenant.UpdatedAt, tenant.DeactivatedAt,
            active?.DatabaseName  ?? string.Empty,
            archive?.DatabaseName ?? string.Empty,
            active?.SchemaVersion));
    }

    // ---------- provision (create) ----------

    [HttpPost]
    public async Task<IActionResult> Provision([FromBody] ProvisionTenantDto dto, CancellationToken ct)
    {
        // TenantProvisioningException is an EnkiException and propagates
        // across the Infrastructure→WebApi boundary to the global handler.
        // We don't catch it here — plumbing a Result<T> up from the
        // provisioning service would fight the existing contract and the
        // occasional-failure profile genuinely is exceptional.
        var result = await provisioning.ProvisionAsync(new ProvisionTenantRequest(
            Code:         dto.Code,
            Name:         dto.Name,
            DisplayName:  dto.DisplayName,
            ContactEmail: dto.ContactEmail,
            Notes:        dto.Notes), ct);

        return CreatedAtAction(nameof(Get), new { code = result.Code }, result);
    }

    // ---------- update ----------

    [HttpPut("{code}")]
    public async Task<IActionResult> Update(
        string code,
        [FromBody] UpdateTenantDto dto,
        CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", code);

        tenant.Name         = dto.Name;
        tenant.DisplayName  = dto.DisplayName;
        tenant.ContactEmail = dto.ContactEmail;
        tenant.Notes        = dto.Notes;
        // UpdatedAt + UpdatedBy are stamped by the DbContext audit
        // interceptor — don't set them manually.

        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- deactivate ----------

    [HttpPost("{code}/deactivate")]
    public async Task<IActionResult> Deactivate(string code, CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", code);

        if (tenant.Status == TenantStatus.Archived)
            return this.ConflictProblem(
                "Archived tenants cannot be deactivated; they are already terminal.");

        if (tenant.Status == TenantStatus.Active)
        {
            tenant.Status        = TenantStatus.Inactive;
            tenant.DeactivatedAt = DateTimeOffset.UtcNow;
            await master.SaveChangesAsync(ct);

            // Bust the resolved-tenant cache so in-flight requests
            // can't continue using the cached connection string for
            // up to 5 minutes after revocation.
            cache.Remove(TenantRoutingMiddleware.CacheKeyFor(code));
        }

        return NoContent();
    }

    // ---------- reactivate ----------

    [HttpPost("{code}/reactivate")]
    public async Task<IActionResult> Reactivate(string code, CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", code);

        if (tenant.Status == TenantStatus.Archived)
            return this.ConflictProblem(
                "Archived tenants cannot be reactivated through this endpoint.");

        if (tenant.Status == TenantStatus.Inactive)
        {
            tenant.Status        = TenantStatus.Active;
            tenant.DeactivatedAt = null;
            await master.SaveChangesAsync(ct);

            // Bust the negative cache entry too — without this, a tenant
            // that was Inactive when last resolved would 404 for up to
            // 5 minutes after reactivation.
            cache.Remove(TenantRoutingMiddleware.CacheKeyFor(code));
        }

        return NoContent();
    }
}
