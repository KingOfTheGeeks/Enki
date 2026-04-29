using System.Text.Json;
using SDI.Enki.Core.Master.Audit;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// Writes a single <see cref="MasterAuditLog"/> row when an
/// authorization handler decides to <c>Fail()</c> a request. Captures
/// the privilege boundary the caller tried to cross — actor sub,
/// target tenant code (if any), policy name — so the master audit
/// feed surfaces "someone tried to do X without the rights" alongside
/// the normal entity-mutation rows.
///
/// <para>
/// <b>Why a dedicated service:</b> the two existing handlers
/// (<c>CanAccessTenantHandler</c>, <c>CanManageTenantMembersHandler</c>)
/// already inject <see cref="EnkiMasterDbContext"/> for membership
/// queries; the future <c>EnkiAdminOnlyHandler</c> needs it
/// purely to write the denial row. Centralising the write means
/// all three speak the same audit shape with one piece of code.
/// </para>
///
/// <para>
/// <b>Failure mode:</b> matches <c>AuthEventLogger</c> — an audit
/// write failure must never affect the authorization decision. The
/// handler has already decided to <c>Fail()</c>; the user gets 403
/// regardless. We just want best-effort observability.
/// </para>
/// </summary>
public interface IAuthzDenialAuditor
{
    Task RecordAsync(
        string  policy,
        string? tenantCode,
        string  actorSub,
        string? reason = null);
}

internal sealed class AuthzDenialAuditor(
    EnkiMasterDbContext master,
    ILogger<AuthzDenialAuditor> logger) : IAuthzDenialAuditor
{
    public async Task RecordAsync(
        string  policy,
        string? tenantCode,
        string  actorSub,
        string? reason = null)
    {
        try
        {
            var detail = JsonSerializer.Serialize(new { policy, tenantCode, reason });
            master.MasterAuditLogs.Add(new MasterAuditLog
            {
                EntityType = "AuthzDenial",
                EntityId   = tenantCode ?? "(global)",
                Action     = "Denied",
                NewValues  = detail,
                ChangedAt  = DateTimeOffset.UtcNow,
                ChangedBy  = actorSub,
            });
            await master.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Best-effort — the user's already getting a 403; lost row
            // is observability cost, not a functional break.
            logger.LogWarning(ex,
                "Failed to write AuthzDenial audit row for {Policy} (tenant={TenantCode}, actor={Actor}); continuing.",
                policy, tenantCode, actorSub);
        }
    }
}
