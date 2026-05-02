using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Authorization;
using SDI.Enki.WebApi.Concurrency;
using SDI.Enki.WebApi.ExceptionHandling;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-level tenant registry endpoints. Not scoped to any one tenant —
/// these operate on the master DB directly. Each action carries its own
/// authorization policy so the read / member-admin / ops-admin tiers are
/// gated separately:
///
/// <list type="bullet">
///   <item><c>List</c> — any authenticated caller, but the response is
///   filtered to tenants the caller is a member of (enki-admin sees all).</item>
///   <item><c>Get</c> — <see cref="EnkiPolicies.CanAccessTenant"/>: tenant
///   member or enki-admin.</item>
///   <item><c>Update</c> — <see cref="EnkiPolicies.CanWriteMasterContent"/>:
///   Office-or-above or enki-admin. Editing display name / contact email
///   is master content, not lifecycle.</item>
///   <item><c>Provision</c> — <see cref="EnkiPolicies.CanProvisionTenants"/>:
///   Supervisor-or-above or enki-admin. Touches ops infrastructure
///   (creates the SQL DB pair).</item>
///   <item><c>Deactivate</c>, <c>Reactivate</c> —
///   <see cref="EnkiPolicies.CanManageTenantLifecycle"/>: Supervisor-
///   or-above or enki-admin. Lifecycle is split from Provision so a
///   future "delegate-deactivate-to-Office" change is one policy edit.</item>
/// </list>
///
/// <para>
/// The route parameter is named <c>tenantCode</c> (not <c>code</c>) so the
/// shared <c>TenantAuthExtractor</c> picks it up — the parametric
/// <c>TeamAuthHandler</c> (which evaluates every <c>EnkiPolicies.*</c>
/// gate) reads <c>RouteValues["tenantCode"]</c> when the requirement is
/// tenant-scoped, and naming the parameter consistently means master-
/// registry endpoints work without per-policy tweaks.
/// </para>
///
/// <para>
/// Error surface: expected failures (unknown code → 404; bad state
/// transition → 409) return ProblemDetails via <see cref="EnkiResults"/>
/// extension methods — cleaner than throwing for known outcomes and
/// doesn't trigger the VS debugger's user-unhandled break on every
/// not-found. Truly unexpected exceptions (DbUpdateConcurrencyException,
/// crashes in deeper layers, <see cref="TenantProvisioningException"/>
/// from Infrastructure) still propagate to <c>EnkiExceptionHandler</c>
/// which produces the same ProblemDetails shape.
/// </para>
/// </summary>
[ApiController]
[Route("tenants")]
[Authorize(Policy = EnkiPolicies.EnkiApiScope)]
[SkipTenantRouting]   // master registry — see attribute docs (issue #23)
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class TenantsController(
    EnkiMasterDbContext master,
    ITenantProvisioningService provisioning,
    IMemoryCache cache) : ControllerBase
{
    // ---------- list ----------

    [HttpGet]
    [ProducesResponseType<IEnumerable<TenantSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IEnumerable<TenantSummaryDto>> List(CancellationToken ct)
    {
        // Filter by membership unless the caller holds the cross-tenant
        // admin role. Without this filter a non-admin saw every tenant
        // in the system on the listing page; the action-level Authorize
        // policies on Get/Update/etc would 403 a click-through, but
        // surfacing the names alone is information leakage.
        var query = master.Tenants.AsNoTracking();

        if (!User.HasEnkiAdminRole())
        {
            // sub is the AspNetUsers.Id (Identity row id). Membership joins
            // through the master User row's IdentityId — same path the
            // TeamAuthHandler uses for its membership check.
            var sub = User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var identityId))
                return Array.Empty<TenantSummaryDto>();

            query = query.Where(t => t.Users
                .Any(tu => tu.User!.IdentityId == identityId));
        }

        var rows = await query
            .OrderBy(t => t.Code)
            .Select(t => new
            {
                t.Id, t.Code, t.Name, t.DisplayName,
                StatusName = t.Status.Name, t.CreatedAt,
                t.RowVersion,
            })
            .ToListAsync(ct);

        return rows.Select(t => new TenantSummaryDto(
            t.Id, t.Code, t.Name, t.DisplayName,
            t.StatusName, t.CreatedAt,
            ConcurrencyHelper.EncodeRowVersion(t.RowVersion)));
    }

    // ---------- detail ----------

    [HttpGet("{tenantCode}")]
    [Authorize(Policy = EnkiPolicies.CanAccessTenant)]
    [ProducesResponseType<TenantDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string tenantCode, CancellationToken ct)
    {
        var tenant = await master.Tenants
            .Include(t => t.Databases)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == tenantCode, ct);

        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        var active  = tenant.Databases.FirstOrDefault(d => d.Kind == TenantDatabaseKind.Active);
        var archive = tenant.Databases.FirstOrDefault(d => d.Kind == TenantDatabaseKind.Archive);

        return Ok(new TenantDetailDto(
            tenant.Id, tenant.Code, tenant.Name, tenant.DisplayName,
            tenant.Status.Name, tenant.ContactEmail, tenant.Notes,
            tenant.CreatedAt, tenant.UpdatedAt, tenant.DeactivatedAt,
            active?.DatabaseName  ?? string.Empty,
            archive?.DatabaseName ?? string.Empty,
            active?.SchemaVersion,
            tenant.EncodeRowVersion()));
    }

    // ---------- provision (create) ----------

    [HttpPost]
    [Authorize(Policy = EnkiPolicies.CanProvisionTenants)]
    [EnableRateLimiting("Expensive")]
    [ProducesResponseType<ProvisionTenantResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
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

        // Bust any negative cache entry left behind by the BlazorServer
        // pre-submit uniqueness probe (see TenantCreate.razor — it does a
        // GET /tenants/{code} to detect duplicates before POSTing here).
        // That probe causes TenantRoutingMiddleware to cache a `null`
        // resolution for up to 5 minutes; without this Remove the very
        // next request after a successful Provision (e.g. the redirect
        // to /tenants/{code}/jobs) hits the stale null and 404s with
        // "Tenant '...' was not found" — issue #21.
        cache.Remove(TenantRoutingMiddleware.CacheKeyFor(result.Code));

        return CreatedAtAction(nameof(Get), new { tenantCode = result.Code }, result);
    }

    // ---------- update ----------

    [HttpPut("{tenantCode}")]
    [Authorize(Policy = EnkiPolicies.CanWriteMasterContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        string tenantCode,
        [FromBody] UpdateTenantDto dto,
        CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        if (this.ApplyClientRowVersion(master, tenant, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        tenant.Name         = dto.Name;
        tenant.DisplayName  = dto.DisplayName;
        tenant.ContactEmail = dto.ContactEmail;
        tenant.Notes        = dto.Notes;
        // UpdatedAt + UpdatedBy are stamped by the DbContext audit
        // interceptor — don't set them manually.

        if (await master.SaveOrConflictAsync(this, "Tenant", ct) is { } conflict)
            return conflict;

        return NoContent();
    }

    // ---------- deactivate ----------

    [HttpPost("{tenantCode}/deactivate")]
    [Authorize(Policy = EnkiPolicies.CanManageTenantLifecycle)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(
        string tenantCode,
        [FromBody] LifecycleTransitionDto dto,
        CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        if (this.ApplyClientRowVersion(master, tenant, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (tenant.Status == TenantStatus.Archived)
            return this.ConflictProblem(
                "Archived tenants cannot be deactivated; they are already terminal.");

        if (tenant.Status == TenantStatus.Active)
        {
            tenant.Status        = TenantStatus.Inactive;
            tenant.DeactivatedAt = DateTimeOffset.UtcNow;
            if (await master.SaveOrConflictAsync(this, "Tenant", ct) is { } conflict)
                return conflict;

            // Bust the resolved-tenant cache so in-flight requests
            // can't continue using the cached connection string for
            // up to 5 minutes after revocation.
            cache.Remove(TenantRoutingMiddleware.CacheKeyFor(tenantCode));
        }

        return NoContent();
    }

    // ---------- reactivate ----------

    [HttpPost("{tenantCode}/reactivate")]
    [Authorize(Policy = EnkiPolicies.CanManageTenantLifecycle)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(
        string tenantCode,
        [FromBody] LifecycleTransitionDto dto,
        CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == tenantCode, ct);
        if (tenant is null) return this.NotFoundProblem("Tenant", tenantCode);

        if (this.ApplyClientRowVersion(master, tenant, dto.RowVersion) is { } badRowVersion)
            return badRowVersion;

        if (tenant.Status == TenantStatus.Archived)
            return this.ConflictProblem(
                "Archived tenants cannot be reactivated through this endpoint.");

        if (tenant.Status == TenantStatus.Inactive)
        {
            tenant.Status        = TenantStatus.Active;
            tenant.DeactivatedAt = null;
            if (await master.SaveOrConflictAsync(this, "Tenant", ct) is { } conflict)
                return conflict;

            // Bust the negative cache entry too — without this, a tenant
            // that was Inactive when last resolved would 404 for up to
            // 5 minutes after reactivation.
            cache.Remove(TenantRoutingMiddleware.CacheKeyFor(tenantCode));
        }

        return NoContent();
    }
}
