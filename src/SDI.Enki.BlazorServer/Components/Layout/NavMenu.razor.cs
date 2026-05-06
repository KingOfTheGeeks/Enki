using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Layout;

public partial class NavMenu : ComponentBase, IDisposable
{
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    private string? _tenantCode;
    private Guid?   _jobId;
    private string? _jobName;
    private int?    _wellId;
    private string? _wellName;
    private Guid?   _runId;
    private string? _runName;
    private Guid?   _modelId;

    // Tracks "may the current user manage this tenant's members?" for
    // the tenant in URL scope. Refreshed on every tenant transition so
    // navigating from /tenants/A to /tenants/B re-evaluates against the
    // new tenant's membership. Default false: the link is hidden until
    // the membership probe completes — Supervisors will see a brief
    // delay before the link appears, which is preferable to flashing
    // the link for users who can't reach the page.
    private bool _canManageMembers;

    // Tracks "may the current user reach this tenant at all?" for the
    // tenant in URL scope. When false the entire tenant-scoped sub-nav
    // (Overview / Jobs / Members / Audit) is hidden so a forbidden
    // direct-URL hit doesn't render a sidebar that pretends the user is
    // inside the tenant. Default false for the same flicker-prevention
    // reason as _canManageMembers.
    private bool _canAccessTenant;
    // _modelName is reserved for the future drill-in marker — see the
    // commented Models nav block above. Will be assigned by a
    // FetchModelNameAsync helper once /tenants/{code}/jobs/{jobId}/models
    // routes land. Suppressing the unused-warning rather than deleting
    // the field keeps the wire-up obvious for the next pass.
#pragma warning disable CS0414
    private string? _modelName;
#pragma warning restore CS0414

    protected override async Task OnInitializedAsync()
    {
        await ApplyContextAsync(Nav.Uri, isInitial: true);
        Nav.LocationChanged += OnLocationChanged;
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // async void on event handler is the documented pattern for
        // NavigationManager.LocationChanged; surface unexpected exceptions
        // through the Blazor renderer rather than silently dropping them.
        await ApplyContextAsync(e.Location, isInitial: false);
    }

    /// <summary>
    /// Re-derives the tenant + job + well + run + model context from
    /// the URL and fetches names for any that changed. Idempotent
    /// across repeat invocations on the same URL — only fires HTTP
    /// when an ID actually moves.
    ///
    /// <para>
    /// <paramref name="isInitial"/> distinguishes the first-render pass
    /// (called from <see cref="OnInitializedAsync"/>) from later
    /// navigation (called from <see cref="OnLocationChanged"/>). On
    /// the initial pass the name fetches are awaited with a tight
    /// timeout so the SSR render lands with resolved labels rather
    /// than the "Job…" / "Well…" / "Run…" placeholders. On subsequent
    /// navigation they stay fire-and-forget — the user is already
    /// looking at content while navigating, so a slow API shouldn't
    /// freeze nav. Mirrors the trade-off the
    /// <see cref="RefreshTenantScopedCapabilitiesAsync"/> call below
    /// already makes for the tenant-scoped capability flags.
    /// </para>
    /// </summary>
    private async Task ApplyContextAsync(string url, bool isInitial)
    {
        var ctx = ExtractContext(url);
        var stateChanged = false;

        // Collected on the initial pass so the SSR render can wait for
        // them; null on subsequent navigation, which keeps the existing
        // fire-and-forget semantics.
        List<Task>? initialNameTasks = isInitial ? new(3) : null;

        if (!string.Equals(ctx.TenantCode, _tenantCode, StringComparison.Ordinal))
        {
            _tenantCode = ctx.TenantCode;
            stateChanged = true;

            // Refresh tenant-scoped capability flags. Cleared first so a
            // user nav-ing from a tenant they manage to one they don't
            // doesn't briefly see the stale "Members" link. Membership
            // probes are cheap after the first call (per-circuit cache).
            _canAccessTenant  = false;
            _canManageMembers = false;
            if (_tenantCode is { Length: > 0 })
            {
                var capturedCode = _tenantCode;
                // Awaited (was fire-and-forget before) so the
                // tenant-scoped sidebar block reflects correct state on
                // the initial SSR render. Without the await, pages
                // without their own data-fetch (the 404 NotFound page,
                // most prominently) race the probe and render with the
                // tenant block hidden — _canAccessTenant defaults false
                // and the probe's StateHasChanged lands after the SSR
                // pipeline has already shipped the response. After the
                // first call the result is cached per-circuit, so
                // subsequent navigations are effectively free.
                //
                // Wrapped in try/catch to preserve the prior behaviour
                // where a probe failure didn't break the page render —
                // the fire-and-forget version dropped exceptions
                // silently. Falling through with _canAccessTenant=false
                // is the safe default (sidebar hides the tenant block
                // until the next nav refreshes; per-route Authorize is
                // still authoritative).
                try
                {
                    await RefreshTenantScopedCapabilitiesAsync(capturedCode);
                }
                catch
                {
                    // Intentionally swallowed — same as the prior
                    // fire-and-forget behaviour. The sidebar stays in
                    // its safe default-deny state.
                }
            }
        }

        if (ctx.JobId != _jobId)
        {
            _jobId   = ctx.JobId;
            _jobName = null;
            stateChanged = true;

            if (ctx.JobId is { } jobId && _tenantCode is { Length: > 0 })
            {
                var task = FetchJobNameAsync(_tenantCode, jobId);
                if (initialNameTasks is not null) initialNameTasks.Add(task);
                else _ = task;
            }
        }

        if (ctx.WellId != _wellId)
        {
            _wellId   = ctx.WellId;
            _wellName = null;
            stateChanged = true;

            if (ctx.WellId is { } wellId
                && ctx.JobId is { } parentJobId
                && _tenantCode is { Length: > 0 })
            {
                var task = FetchWellNameAsync(_tenantCode, parentJobId, wellId);
                if (initialNameTasks is not null) initialNameTasks.Add(task);
                else _ = task;
            }
        }

        if (ctx.RunId != _runId)
        {
            _runId   = ctx.RunId;
            _runName = null;
            stateChanged = true;

            if (ctx.RunId is { } runId
                && ctx.JobId is { } parentJobId
                && _tenantCode is { Length: > 0 })
            {
                var task = FetchRunNameAsync(_tenantCode, parentJobId, runId);
                if (initialNameTasks is not null) initialNameTasks.Add(task);
                else _ = task;
            }
        }

        if (ctx.ModelId != _modelId)
        {
            _modelId   = ctx.ModelId;
            _modelName = null;
            stateChanged = true;

            // Models routes don't exist yet — when they do, drop in a
            // FetchModelNameAsync mirror of the Run helper. Until then
            // ModelId stays null on every URL because no /models/{guid}
            // segment ever matches.
        }

        // Initial pass: await the name fetches so the SSR render lands
        // with resolved labels instead of "Job…" / "Well…" / "Run…"
        // placeholders. Tight timeout means a slow / down API still
        // ships the page; the placeholders fall back in and each
        // helper's own StateHasChanged updates the sidebar when the
        // slow call eventually completes. Catch swallows everything
        // (TimeoutException, network errors, transient auth failures)
        // to mirror the prior fire-and-forget behaviour — each
        // helper's own success guard (_jobId == jobId etc.) handles
        // the late-arrival case correctly.
        if (initialNameTasks is { Count: > 0 })
        {
            try
            {
                await Task.WhenAll(initialNameTasks)
                          .WaitAsync(TimeSpan.FromMilliseconds(800));
            }
            catch
            {
            }
        }

        if (stateChanged)
            await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Re-evaluates the tenant-scoped predicates against the
    /// <paramref name="tenantCode"/> in URL scope. Stale-fetch guard:
    /// the answer is only applied if the user is still on that tenant
    /// when the probe completes — otherwise the result belongs to a
    /// tenant they've already navigated away from.
    /// </summary>
    private async Task RefreshTenantScopedCapabilitiesAsync(string tenantCode)
    {
        // Two probes in parallel — both hit the same per-circuit
        // membership cache, so the second is effectively free.
        // CanAccessTenantAsync is the gate for the whole tenant-scoped
        // sub-nav; CanManageTenantMembersAsync is the inner gate for
        // the Members link.
        var accessTask = Capabilities.CanAccessTenantAsync(tenantCode);
        var manageTask = Capabilities.CanManageTenantMembersAsync(tenantCode);
        await Task.WhenAll(accessTask, manageTask);

        if (string.Equals(_tenantCode, tenantCode, StringComparison.Ordinal))
        {
            _canAccessTenant  = accessTask.Result;
            _canManageMembers = manageTask.Result;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task FetchJobNameAsync(string tenantCode, Guid jobId)
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<JobDetailDto>(
            $"tenants/{tenantCode}/jobs/{jobId}");

        // Stale-fetch guard: by the time the response lands the user
        // may have navigated to another job. Only apply when the ID
        // we fetched is still the current context. Failure swallowed —
        // the placeholder "Job…" stays visible. Sidebar is non-blocking;
        // failing here can't break navigation.
        if (result.IsSuccess && _jobId == jobId)
        {
            _jobName = result.Value.Name;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task FetchWellNameAsync(string tenantCode, Guid jobId, int wellId)
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<WellDetailDto>(
            $"tenants/{tenantCode}/jobs/{jobId}/wells/{wellId}");

        if (result.IsSuccess && _wellId == wellId)
        {
            _wellName = result.Value.Name;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task FetchRunNameAsync(string tenantCode, Guid jobId, Guid runId)
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<RunDetailDto>(
            $"tenants/{tenantCode}/jobs/{jobId}/runs/{runId}");

        if (result.IsSuccess && _runId == runId)
        {
            _runName = result.Value.Name;
            await InvokeAsync(StateHasChanged);
        }
    }

    private readonly record struct UrlContext(
        string? TenantCode,
        Guid?   JobId,
        int?    WellId,
        Guid?   RunId,
        Guid?   ModelId);

    /// <summary>
    /// Pulls <c>{tenantCode}</c>, <c>{jobId}</c>, <c>{wellId}</c>,
    /// <c>{runId}</c>, and <c>{modelId}</c> segments out of a URL of
    /// the shape
    /// <c>/tenants/{code}/jobs/{guid}/(wells/{int}|runs/{guid}|models/{guid})/...</c>.
    /// Each segment is independent — a missing well doesn't blank the
    /// job, a missing job doesn't blank the tenant. The literal
    /// "tenants" / "jobs" / "wells" / "runs" / "models" segments match
    /// case-insensitively; ID segments parse strictly so "/jobs/new",
    /// "/wells/import", etc. don't masquerade as IDs.
    /// </summary>
    private static UrlContext ExtractContext(string url)
    {
        var path = url;
        var schemeIdx = path.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
        {
            var slash = path.IndexOf('/', schemeIdx + 3);
            path = slash >= 0 ? path[slash..] : "/";
        }
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];

        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2) return default;
        if (!string.Equals(parts[0], "tenants", StringComparison.OrdinalIgnoreCase)) return default;
        if (string.Equals(parts[1], "new", StringComparison.OrdinalIgnoreCase)) return default;

        var code = parts[1];

        Guid? jobId = null;
        if (parts.Length >= 4
            && string.Equals(parts[2], "jobs", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[3], out var parsedJobId))
        {
            jobId = parsedJobId;
        }

        // /tenants/{code}/jobs/{guid}/wells/{int}/...
        int? wellId = null;
        if (jobId is not null
            && parts.Length >= 6
            && string.Equals(parts[4], "wells", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[5], out var parsedWellId))
        {
            wellId = parsedWellId;
        }

        // /tenants/{code}/jobs/{guid}/runs/{guid}/...
        // Runs / Wells / Models are siblings — at most one is in scope at a time.
        Guid? runId = null;
        if (jobId is not null
            && parts.Length >= 6
            && string.Equals(parts[4], "runs", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[5], out var parsedRunId))
        {
            runId = parsedRunId;
        }

        // /tenants/{code}/jobs/{guid}/models/{guid}/...
        Guid? modelId = null;
        if (jobId is not null
            && parts.Length >= 6
            && string.Equals(parts[4], "models", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[5], out var parsedModelId))
        {
            modelId = parsedModelId;
        }

        return new UrlContext(code, jobId, wellId, runId, modelId);
    }

    public void Dispose() => Nav.LocationChanged -= OnLocationChanged;
}
