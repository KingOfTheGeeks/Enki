using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class AuditHistoryTile : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    /// <summary>Tenant code in the URL — drives both the API call and the cross-link to the full tenant audit feed.</summary>
    [Parameter, EditorRequired] public string TenantCode { get; set; } = "";

    /// <summary>CLR class name of the audited entity (e.g. <c>Survey</c>, <c>Job</c>, <c>Well</c>, <c>Tenant</c>).</summary>
    [Parameter, EditorRequired] public string EntityType { get; set; } = "";

    /// <summary>Primary-key value of the audited row, serialised to string.</summary>
    [Parameter, EditorRequired] public string EntityId { get; set; } = "";

    /// <summary>
    /// Maximum rows to pull from the API. Default 500 — which matches
    /// the server's hard ceiling, so the grid below shows essentially
    /// "everything for this entity (or subtree)" with client-side
    /// paging / sorting / filtering doing the rest. Override only when
    /// a host needs a smaller, fixed window (rare).
    /// </summary>
    [Parameter] public int Take { get; set; } = 500;

    /// <summary>User-facing noun rendered in the tile body (e.g. "well", "job"). Defaults to <see cref="EntityType"/> lower-cased.</summary>
    [Parameter] public string? FriendlyEntityName { get; set; }

    private IReadOnlyList<AuditLogEntryDto>? _entries;
    private int _total;
    private string? _loadError;
    private bool _revealed;
    private bool _busy;
    private bool _fetched;   // suppresses re-fetch when re-revealing after a hide

    /// <summary>
    /// Container types whose detail page rolls up the subtree audit
    /// trail. Drives both the subtitle copy and the includeChildren
    /// API call. Mirrors the server-side switch in
    /// <c>AuditController.ResolveSubtreePairsAsync</c>.
    /// <para>
    /// Note: <c>Job</c> is intentionally excluded. A job's children
    /// (wells, runs) have their own detail pages with their own roll-up
    /// tiles, and the per-Job page should only show changes to the Job
    /// row itself — title, description, unit system. Otherwise the Job
    /// tile would dwarf the Well and Run tiles by including everything
    /// they show plus the surveys/tubulars/shots underneath.
    /// </para>
    /// </summary>
    private bool HasChildren => EntityType is "Well" or "Run";

    private string ButtonLabel => _busy
        ? "Loading…"
        : _revealed
            ? "Hide activity"
            : "Show recent activity";

    protected override void OnInitialized()
    {
        FriendlyEntityName ??= EntityType.ToLowerInvariant();
    }

    private async Task ToggleAsync()
    {
        if (_busy) return;

        if (_revealed)
        {
            _revealed = false;
            return;
        }

        _revealed = true;
        if (_fetched) return;   // already loaded; just re-show the cached data

        await FetchAsync();
    }

    private async Task FetchAsync()
    {
        _busy = true;
        _loadError = null;
        StateHasChanged();

        try
        {
            // includeChildren is gated on HasChildren so non-rolling-up
            // hosts (Tenant, Job, leaf entities) only fetch the entity's
            // own audit rows. The principle: each tile rolls up the
            // smallest group around itself; entities whose children
            // already own a detail page (Tenant→Jobs, Job→Wells/Runs)
            // don't fan out, so the timeline stays focused.
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var includeChildren = HasChildren ? "true" : "false";
            var path = $"tenants/{TenantCode}/audit/{Uri.EscapeDataString(EntityType)}/{Uri.EscapeDataString(EntityId)}" +
                       $"?take={Take}&includeChildren={includeChildren}";

            var result = await client.GetAsync<PagedResult<AuditLogEntryDto>>(path);
            if (!result.IsSuccess)
            {
                // Audit fetch failure is non-fatal — the rest of the page
                // still renders. Show a short banner inside the panel and
                // leave the panel open so the user can retry by hide+show.
                _loadError = $"Couldn't load audit history: {result.Error.AsAlertText()}";
                _entries = Array.Empty<AuditLogEntryDto>();
                return;
            }

            _entries = result.Value.Items;
            _total = result.Value.Total;
            _fetched = true;
        }
        finally
        {
            _busy = false;
        }
    }
}
