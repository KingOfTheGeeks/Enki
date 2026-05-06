using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/plot")]
[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/plot")]
[Authorize]
public partial class WellsPlot : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    /// <summary>
    /// When set, the chart filters to a single well — same data,
    /// same component, smaller series collection. Null when the
    /// route is the multi-well overlay.
    /// </summary>
    [Parameter] public int?   WellId     { get; set; }

    private List<WellTrajectoryDto>? _trajectories;
    private string? _error;
    private string? JobName;
    private UnitSystem _units = UnitSystem.SI;

    /// <summary>
    /// Active tab. Toggled by the tab strip at the top of the
    /// rendered output; both views share the same fetched data
    /// and the same series colour / legend choices.
    /// </summary>
    private PlotView _view = PlotView.Plan;

    private enum PlotView
    {
        /// <summary>Top-down Easting × Northing.</summary>
        Plan,
        /// <summary>Side elevation: V-sect × TVD with TVD increasing downward.</summary>
        Vertical,
        /// <summary>
        /// Travelling cylinder: closest-approach distance from one
        /// target well to every other well in the Job, plotted vs
        /// the target's MD. Lazily-loaded (anti-collision scan is
        /// per-target so it lives on a separate fetch from the
        /// trajectories payload).
        /// </summary>
        Cylinder,
    }

    // ---------- cylinder-tab state ----------
    //
    // The cylinder tab is fetched separately from the trajectories
    // payload — the anti-collision scan is per-target, more
    // expensive than the cached-trajectories read, and not every
    // user opens this tab. So we load on demand.

    /// <summary>Currently-selected target well for the cylinder scan.</summary>
    private int? _cylinderTargetId;

    /// <summary>Result of the most recent scan; null until first load.</summary>
    private List<AntiCollisionScanDto>? _cylinderScans;

    /// <summary>True while a scan is in flight; disables the picker.</summary>
    private bool _cylinderLoading;

    /// <summary>Error from the most recent scan, surfaced in the tab.</summary>
    private string? _cylinderError;

    /// <summary>Display name of the current cylinder target — chart title.</summary>
    private string? CylinderTargetName =>
        _trajectories?.FirstOrDefault(w => w.Id == _cylinderTargetId)?.Name;

    /// <summary>
    /// Points filtered to <see cref="WellId"/> when set, otherwise
    /// the full multi-well list. Pre-computed so the render
    /// expression doesn't re-filter on every call.
    /// </summary>
    private IReadOnlyList<WellTrajectoryDto> FilteredWells =>
        _trajectories is null
            ? Array.Empty<WellTrajectoryDto>()
            : WellId is { } wid
                ? _trajectories.Where(w => w.Id == wid).ToList()
                : _trajectories;

    private bool HasNoPlottableData =>
        FilteredWells.All(w => w.Points.Count == 0);

    /// <summary>"plan view" / "vertical section" / "travelling cylinder" — used by both the
    /// PageTitle and the in-page H1 / subtitle so the page header
    /// follows the active tab.</summary>
    private string ViewLabel => _view switch
    {
        PlotView.Plan     => "plan view",
        PlotView.Vertical => "vertical section",
        PlotView.Cylinder => "travelling cylinder",
        _                 => "plan view",
    };

    private string PageTitle => (WellId, FilteredWells.FirstOrDefault()?.Name) switch
    {
        (null, _)                     => $"Wells {ViewLabel}",
        (_,    { Length: > 0 } name)  => $"{name} — {ViewLabel}",
        _                             => $"Well {ViewLabel}",
    };

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Job + trajectories fetched in parallel — independent
        // calls. Job's UnitSystem drives the axis units; the
        // trajectories list IS the chart data.
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var trajTask = client.GetAsync<List<WellTrajectoryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/trajectories");

        await Task.WhenAll(jobTask, trajTask);

        var jobResult  = jobTask.Result;
        var trajResult = trajTask.Result;

        if (!jobResult.IsSuccess)  { _error = jobResult.Error.AsAlertText();  return; }
        if (!trajResult.IsSuccess) { _error = trajResult.Error.AsAlertText(); return; }

        _units        = await UnitPrefs.ResolveAsync(jobResult.Value.UnitSystem);
        JobName       = jobResult.Value.Name;
        _trajectories = trajResult.Value;
    }

    /// <summary>
    /// Plan-view projection: SI metres → display units, packed as
    /// (X = Easting, Y = Northing). Looking down at the well, north
    /// up, east right.
    /// </summary>
    private List<PlotPoint> ProjectPlanPoints(IReadOnlyList<TrajectoryPointDto> points)
    {
        var result = new List<PlotPoint>(points.Count);
        foreach (var p in points)
        {
            var n = Measurement.FromSi(p.Northing, EnkiQuantity.Length).As(_units).Value;
            var e = Measurement.FromSi(p.Easting,  EnkiQuantity.Length).As(_units).Value;
            result.Add(new PlotPoint(e, n));
        }
        return result;
    }

    /// <summary>
    /// Vertical-section projection: SI metres → display units,
    /// packed as (X = VerticalSection, Y = TVD). The Y axis on the
    /// chart itself is inversed so depth reads downward, but the
    /// data values stay positive — drillers look at TVD as a
    /// magnitude, the inversion is purely visual.
    /// </summary>
    private List<PlotPoint> ProjectVerticalPoints(IReadOnlyList<TrajectoryPointDto> points)
    {
        var result = new List<PlotPoint>(points.Count);
        foreach (var p in points)
        {
            var v = Measurement.FromSi(p.VerticalSection, EnkiQuantity.Length).As(_units).Value;
            var d = Measurement.FromSi(p.Tvd,             EnkiQuantity.Length).As(_units).Value;
            result.Add(new PlotPoint(v, d));
        }
        return result;
    }

    /// <summary>
    /// Travelling-cylinder projection: SI metres → display units,
    /// packed as (X = closest-approach distance, Y = target MD).
    /// Y axis is inversed at the chart level so MD runs downward,
    /// matching the V-sect chart's TVD convention. Clock position
    /// rides through unprojected — degrees are universal — and
    /// surfaces in the tooltip via the chart format string.
    /// </summary>
    private List<PlotPoint> ProjectCylinderPoints(IReadOnlyList<AntiCollisionSampleDto> samples)
    {
        var result = new List<PlotPoint>(samples.Count);
        foreach (var s in samples)
        {
            var dist = Measurement.FromSi(s.Distance, EnkiQuantity.Length).As(_units).Value;
            var md   = Measurement.FromSi(s.TargetMd, EnkiQuantity.Length).As(_units).Value;
            result.Add(new PlotPoint(dist, md));
        }
        return result;
    }

    /// <summary>
    /// First-click activator for the cylinder tab. Picks an initial
    /// target if none chosen yet (route's WellId, else the first
    /// Target-typed well, else just the first well alphabetical),
    /// fires the scan, and flips <see cref="_view"/> regardless —
    /// the chart-area branch handles loading / error / empty states
    /// itself.
    /// </summary>
    private async Task ActivateCylinderAsync()
    {
        _view = PlotView.Cylinder;

        if (_cylinderTargetId is null && _trajectories is { Count: > 0 } wells)
        {
            // Prefer the route's well, then a Target-typed well, then
            // the alphabetical first. Picking a Target-typed default
            // matches what the user almost always wants — the
            // producer is the well being drilled, the others are the
            // ones you're trying not to hit.
            _cylinderTargetId =
                WellId
                ?? wells.FirstOrDefault(w => w.Type == "Target")?.Id
                ?? wells.OrderBy(w => w.Name).First().Id;
        }

        if (_cylinderTargetId is not null && _cylinderScans is null)
            await ReloadCylinderAsync();
    }

    /// <summary>
    /// Re-runs the anti-collision scan for the current
    /// <see cref="_cylinderTargetId"/>. Fired by the picker's bind:after
    /// callback, and by the initial activation. Errors surface
    /// inline in the tab; the chart and the picker stay rendered
    /// so the user can pick a different target without losing the
    /// page.
    /// </summary>
    private async Task ReloadCylinderAsync()
    {
        if (_cylinderTargetId is null) return;

        _cylinderLoading = true;
        _cylinderError   = null;
        _cylinderScans   = null;
        StateHasChanged();

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var url = $"tenants/{TenantCode}/jobs/{JobId}/wells/{_cylinderTargetId}/anti-collision";
        var result = await client.GetAsync<List<AntiCollisionScanDto>>(url);

        if (result.IsSuccess) _cylinderScans = result.Value;
        else                  _cylinderError = result.Error.AsAlertText();

        _cylinderLoading = false;
        StateHasChanged();
    }

    /// <summary>
    /// Map a Well's type to its line color. Target reads as the
    /// HUD-accent so the producer always pops; Intercept is a
    /// muted blue (operational sibling); Offset is grey because
    /// it's the anti-collision "watch out" reference, not the
    /// thing being drilled.
    /// </summary>
    private static string ColorFor(string wellType) => wellType switch
    {
        "Target"    => "#5dd5ff",   // matches --enki-accent
        "Intercept" => "#5d8fff",   // muted blue
        "Offset"    => "#8a8a8a",   // dim grey
        _           => "#cccccc",
    };

    /// <summary>
    /// Tiny chart-series row. SfChart wants distinct property names
    /// for X / Y (passed via XName / YName as nameof references),
    /// so an anonymous tuple won't do.
    /// </summary>
    public sealed record PlotPoint(double X, double Y);
}
