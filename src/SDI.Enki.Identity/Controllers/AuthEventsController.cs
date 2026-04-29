using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.Identity.Controllers;

/// <summary>
/// Read-only feed for authentication events — sign-ins, sign-outs,
/// token issuance, lockouts. Sister to <see cref="IdentityAuditController"/>
/// (which handles "admin did X to user Y") and the master-side
/// <c>MasterAuditController</c>; this surface is "user attempted
/// authentication, here is what happened."
///
/// <para>
/// Filters: <c>username</c> (exact match), <c>eventType</c> (exact
/// match), <c>from</c> / <c>to</c> (date range). All optional.
/// CSV export at <c>/csv</c> mirrors the master + tenant audit
/// twins — full matching set, no pagination.
/// </para>
///
/// <para>
/// Auth: same as <see cref="AdminUsersController"/> — OpenIddict
/// validation scheme + <c>EnkiAdmin</c> policy. The auth-event log
/// is sensitive (IPs, usernames, attack patterns) and is SDI-only
/// by definition.
/// </para>
/// </summary>
[ApiController]
[Route("admin/audit/auth-events")]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = "EnkiAdmin")]
public sealed class AuthEventsController(ApplicationDbContext db) : ControllerBase
{
    private const int DefaultTake = 50;
    private const int MaxTake     = 500;

    [HttpGet]
    public async Task<PagedResult<AuthEventEntryDto>> List(
        [FromQuery] string? username = null,
        [FromQuery] string? eventType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        var pageSkip = Math.Max(0, skip ?? 0);
        var pageTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        var query = ApplyFilters(db.AuthEventLogs.AsNoTracking(),
            username, eventType, from, to);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip(pageSkip)
            .Take(pageTake)
            .Select(e => new AuthEventEntryDto(
                e.Id, e.EventType, e.Username, e.IdentityId,
                e.IpAddress, e.UserAgent, e.Detail, e.OccurredAt))
            .ToListAsync(ct);

        return new PagedResult<AuthEventEntryDto>(rows, total, pageSkip, pageTake);
    }

    [HttpGet("csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string? username = null,
        [FromQuery] string? eventType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var query = ApplyFilters(db.AuthEventLogs.AsNoTracking(),
            username, eventType, from, to);

        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new AuthEventEntryDto(
                e.Id, e.EventType, e.Username, e.IdentityId,
                e.IpAddress, e.UserAgent, e.Detail, e.OccurredAt))
            .ToListAsync(ct);

        var csv = AuditCsv.Serialize(rows,
        [
            ("Id",         r => r.Id),
            ("OccurredAt", r => r.OccurredAt),
            ("EventType",  r => r.EventType),
            ("Username",   r => r.Username),
            ("IdentityId", r => r.IdentityId),
            ("IpAddress",  r => r.IpAddress),
            ("UserAgent",  r => r.UserAgent),
            ("Detail",     r => r.Detail),
        ]);

        return new FileContentResult(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv")
        {
            FileDownloadName = $"enki-auth-events-{DateTimeOffset.UtcNow:yyyy-MM-dd}.csv",
        };
    }

    private static IQueryable<AuthEventLog> ApplyFilters(
        IQueryable<AuthEventLog> q,
        string? username,
        string? eventType,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (!string.IsNullOrWhiteSpace(username))  q = q.Where(e => e.Username == username);
        if (!string.IsNullOrWhiteSpace(eventType)) q = q.Where(e => e.EventType == eventType);
        if (from is { } f)                         q = q.Where(e => e.OccurredAt >= f);
        if (to   is { } t)                         q = q.Where(e => e.OccurredAt <  t);
        return q;
    }
}
