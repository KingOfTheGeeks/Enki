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
/// </summary>
[ApiController]
[Route("tenants")]
[Authorize(Policy = "EnkiApiScope")]
public sealed class TenantsController(
    AthenaMasterDbContext master,
    ITenantProvisioningService provisioning) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<TenantSummaryDto>> List(CancellationToken ct) =>
        await master.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new TenantSummaryDto(
                t.Id, t.Code, t.Name, t.DisplayName,
                t.Status.Name, t.Region, t.CreatedAt))
            .ToListAsync(ct);

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
            tenant.Status.Name, tenant.Region, tenant.ContactEmail, tenant.Notes,
            tenant.CreatedAt, tenant.UpdatedAt, tenant.DeactivatedAt,
            active?.DatabaseName  ?? string.Empty,
            archive?.DatabaseName ?? string.Empty,
            active?.SchemaVersion));
    }

    [HttpPost]
    public async Task<IActionResult> Provision([FromBody] ProvisionTenantDto dto, CancellationToken ct)
    {
        try
        {
            var result = await provisioning.ProvisionAsync(new ProvisionTenantRequest(
                Code:         dto.Code,
                Name:         dto.Name,
                DisplayName:  dto.DisplayName,
                Region:       dto.Region,
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
}
