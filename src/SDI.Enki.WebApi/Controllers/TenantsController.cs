using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.WebApi.Controllers;

/// <summary>
/// Master-level tenant registry endpoints. Not scoped to any one tenant —
/// these operate on the master DB directly. Currently requires any caller
/// with an enki-scope bearer token; admin-only gating (policy "EnkiAdmin"
/// checking TenantUser.Role == Admin) comes in a follow-up pass.
///
/// Provisioning (POST) is destructive on the SQL Server side and goes
/// through the <see cref="ITenantProvisioningService"/> so it can create
/// the Active + Archive database pair, apply migrations, and record the
/// MigrationRun audit rows. Updates / deactivate / reactivate are pure
/// master-DB edits.
/// </summary>
[ApiController]
[Route("tenants")]
[Authorize(Policy = "EnkiApiScope")]
public sealed class TenantsController(
    AthenaMasterDbContext master,
    ITenantProvisioningService provisioning) : ControllerBase
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

        if (tenant is null) return NotFound();

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
        try
        {
            var result = await provisioning.ProvisionAsync(new ProvisionTenantRequest(
                Code:         dto.Code,
                Name:         dto.Name,
                DisplayName:  dto.DisplayName,
                ContactEmail: dto.ContactEmail,
                Notes:        dto.Notes), ct);

            return CreatedAtAction(nameof(Get), new { code = result.Code }, result);
        }
        catch (TenantProvisioningException ex)
        {
            return BadRequest(new
            {
                error = ex.Message,
                partialTenantId = ex.PartialTenantId,
            });
        }
    }

    // ---------- update ----------

    /// <summary>
    /// Updates the mutable fields on a tenant. Full-replacement semantics —
    /// any field not set in the body reverts to null (apart from Name which
    /// is required). Code and Status are immutable through this endpoint;
    /// use the /deactivate and /reactivate operations for status changes.
    /// </summary>
    [HttpPut("{code}")]
    public async Task<IActionResult> Update(
        string code,
        [FromBody] UpdateTenantDto dto,
        CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tenant is null) return NotFound();

        tenant.Name         = dto.Name;
        tenant.DisplayName  = dto.DisplayName;
        tenant.ContactEmail = dto.ContactEmail;
        tenant.Notes        = dto.Notes;
        tenant.UpdatedAt    = DateTimeOffset.UtcNow;

        await master.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- deactivate ----------

    /// <summary>
    /// Flips an Active tenant to Inactive and stamps <c>DeactivatedAt</c>.
    /// Idempotent on an already-Inactive tenant. Archived tenants are
    /// rejected — they've already been through the archive move and
    /// shouldn't round-trip back through Inactive.
    /// </summary>
    [HttpPost("{code}/deactivate")]
    public async Task<IActionResult> Deactivate(string code, CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tenant is null) return NotFound();

        if (tenant.Status == TenantStatus.Archived)
            return Conflict(new { error = "Archived tenants cannot be deactivated; they are already terminal." });

        if (tenant.Status == TenantStatus.Active)
        {
            tenant.Status        = TenantStatus.Inactive;
            tenant.DeactivatedAt = DateTimeOffset.UtcNow;
            tenant.UpdatedAt     = DateTimeOffset.UtcNow;
            await master.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    // ---------- reactivate ----------

    /// <summary>
    /// Flips an Inactive tenant back to Active and clears
    /// <c>DeactivatedAt</c>. Idempotent on an already-Active tenant.
    /// Archived tenants are rejected — reactivation would need the
    /// archive-to-active move, which is a separate operation.
    /// </summary>
    [HttpPost("{code}/reactivate")]
    public async Task<IActionResult> Reactivate(string code, CancellationToken ct)
    {
        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (tenant is null) return NotFound();

        if (tenant.Status == TenantStatus.Archived)
            return Conflict(new { error = "Archived tenants cannot be reactivated through this endpoint." });

        if (tenant.Status == TenantStatus.Inactive)
        {
            tenant.Status        = TenantStatus.Active;
            tenant.DeactivatedAt = null;
            tenant.UpdatedAt     = DateTimeOffset.UtcNow;
            await master.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
