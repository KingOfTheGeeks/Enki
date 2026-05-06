using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Paging;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.Shared.Wells.TieOns;
using Syncfusion.Blazor.Grids;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/surveys")]
[Authorize]
public partial class Surveys : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }

    private List<GridRow>? _rows;
    private TieOnSummaryDto? _tieOn;
    private bool _busy;
    private string? _saveStatus;
    private string _saveAlertClass = "alert-info";
    private int    _surveyCount;
    private double _minDepth;
    private double _maxDepth;
    private int    _surveysTotal;
    private bool   _surveysHasMore;
    private string? _error;

    /// <summary>
    /// Grid handle used by <see cref="TieOnCellBlurAsync"/> to force a
    /// cell-level repaint after the survey rows are mutated in place
    /// — Syncfusion otherwise skips re-rendering when the data-source
    /// reference and row identities are unchanged, so the recomputed
    /// trajectory columns wouldn't paint until the next full reload.
    /// <c>Refresh()</c> repaints visible cells without disturbing the
    /// open editor on the in-edit row.
    /// </summary>
    private SfGrid<GridRow>? _grid;

    /// <summary>
    /// Debounce token for the tie-on auto-save on cell blur. Cancelled
    /// on every fresh blur so a flurry of rapid Tab-throughs collapses
    /// into a single save once typing settles for the debounce window
    /// (200 ms — short enough to feel responsive, long enough that
    /// tabbing across the seven editable tie-on cells is one save).
    /// </summary>
    private CancellationTokenSource? _tieOnSaveCts;

    /// <summary>
    /// Serialises tie-on PUTs so a slow save (~half a second on a
    /// cold API) doesn't get passed by a quicker one queued behind it
    /// — the second save would otherwise PUT with the pre-recalc
    /// RowVersion and 409.
    /// </summary>
    private readonly SemaphoreSlim _tieOnSaveSemaphore = new(1, 1);

    /// <summary>
    /// The Job's preferred display unit-system. Drives the per-column
    /// HeaderText abbreviations via <see cref="UnitLabel"/>. Defaults
    /// to strict SI so the first paint (before the Job fetch returns)
    /// shows the raw stored unit names rather than guessing wrong.
    /// </summary>
    private UnitSystem _units = UnitSystem.SI;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Pull all three calls in parallel — they're independent
        // and the Job + tie-on calls are small. WhenAll keeps
        // page-load fast. The Job fetch is here to learn the
        // UnitSystem for column-header abbreviations + to feed
        // the <CascadingValue> wrapping the grid (so cell-level
        // <UnitFormatted> / <UnitInput> templates pick it up).
        // Same pattern repeats across every Wells-area page.
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var tieOnsTask = client.GetAsync<List<TieOnSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons");
        // Surveys list is paged at the wire layer; default take=2000
        // covers any single-well dataset we've observed. If a tenant
        // ever lands a well with > 2000 stations, the page hint at the
        // bottom of the grid surfaces the truncation; that's a
        // forward-looking signal more than a current concern.
        var surveysTask = client.GetAsync<PagedResult<SurveySummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");

        await Task.WhenAll(jobTask, tieOnsTask, surveysTask);

        var jobResult     = jobTask.Result;
        var tieOnsResult  = tieOnsTask.Result;
        var surveysResult = surveysTask.Result;

        if (!jobResult.IsSuccess)     { _error = jobResult.Error.AsAlertText();     return; }
        if (!tieOnsResult.IsSuccess)  { _error = tieOnsResult.Error.AsAlertText();  return; }
        if (!surveysResult.IsSuccess) { _error = surveysResult.Error.AsAlertText(); return; }

        _units      = await UnitPrefs.ResolveAsync(jobResult.Value.UnitSystem);
        var tieOns  = tieOnsResult.Value;
        var surveys = surveysResult.Value.Items;
        _surveysTotal     = surveysResult.Value.Total;
        _surveysHasMore   = surveysResult.Value.HasMore;

        // Stat tiles report the total well stations (server-truth),
        // not just the page slice. Min / max are computed against the
        // page since they're an at-a-glance summary of what the user
        // is looking at; truncation is signalled separately by the
        // paging hint below the grid.
        _surveyCount = _surveysTotal;
        _minDepth    = surveys.Count == 0 ? 0 : surveys.Min(s => s.Depth);
        _maxDepth    = surveys.Count == 0 ? 0 : surveys.Max(s => s.Depth);

        // Anchor on the lowest-Id tie-on (matches what the auto-calc
        // does server-side).
        _tieOn = tieOns.OrderBy(t => t.Id).FirstOrDefault();

        // Build grid rows: tie-on first (when present), then surveys
        // in depth order (the API already returns them sorted).
        _rows = new List<GridRow>(surveys.Count + 1);
        if (_tieOn is not null) _rows.Add(MapTieOn(_tieOn));
        _rows.AddRange(surveys.Select(MapSurvey));
    }

    /// <summary>
    /// Both tie-on and survey rows enter inline edit on double-click.
    /// The Save handler in <see cref="OnActionCompleteAsync"/> branches
    /// on <see cref="GridRow.IsTieOn"/> to PUT to the right controller;
    /// the auto-calc fires server-side after either path, so the page
    /// force-reload below picks up the recomputed trajectory.
    /// </summary>
    private void OnActionBegin(ActionEventArgs<GridRow> args)
    {
        // Intentionally empty — kept as a hook for future row-level
        // validation. Removing the wiring would also remove the place
        // we'd add it.
    }

    /// <summary>
    /// Save handler — fires when the user presses Enter (or clicks the
    /// toolbar Update button) on either a tie-on or a survey edit.
    /// Tie-on rows PUT to TieOnsController; survey rows PUT to
    /// SurveysController. Both endpoints fire the server-side
    /// <c>ISurveyAutoCalculator</c> after the field mutation, so a
    /// force-reload below picks up the recomputed trajectory columns
    /// (TVD / N / E / DLS / V-sect / Build / Turn) on every row.
    /// </summary>
    private async Task OnActionCompleteAsync(ActionEventArgs<GridRow> args)
    {
        if (args.RequestType != Syncfusion.Blazor.Grids.Action.Save) return;
        if (args.Data is not { } row) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var saveResult = row.IsTieOn
            ? await SaveTieOnAsync(client, row)
            : await SaveSurveyAsync(client, row);

        if (saveResult.IsSuccess)
        {
            // In-place re-fetch instead of Nav.NavigateTo(forceLoad: true).
            // The auto-calc may have recomputed downstream rows; LoadAsync
            // pulls the fresh server state and Syncfusion rebinds smoothly,
            // preserving scroll/selection. Avoids the full circuit teardown
            // (auth re-read + Razor re-render + grid re-init) that the old
            // forceLoad path triggered, which surfaced as page-jitter on
            // every survey / tie-on save.
            await LoadAsync();
            StateHasChanged();
            return;
        }

        _saveStatus     = saveResult.Error.AsAlertText();
        _saveAlertClass = "alert-danger";
    }

    private async Task<ApiResult> SaveTieOnAsync(HttpClient client, GridRow row)
    {
        if (_tieOn is null)
            return ApiResult.Failure(new ApiError(
                StatusCode:  0,
                Kind:        ApiErrorKind.Unknown,
                Title:       "No tie-on loaded — refresh the page and retry.",
                Detail:      null,
                FieldErrors: null));

        var dto = new UpdateTieOnDto(
            Depth:                    row.Depth,
            Inclination:              row.Inclination,
            Azimuth:                  row.Azimuth,
            North:                    0,
            East:                     0,
            Northing:                 row.Northing,
            Easting:                  row.Easting,
            VerticalReference:        row.VerticalDepth,    // tie-on's TVD column carries VR
            SubSeaReference:          row.SubSea,            // and Sub-sea carries SubSeaReference
            VerticalSectionDirection: _tieOn.VerticalSectionDirection,
            RowVersion:               row.RowVersion);

        return await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons/{_tieOn.Id}",
            dto);
    }

    private async Task<ApiResult> SaveSurveyAsync(HttpClient client, GridRow row)
    {
        // Only the three observed fields are accepted by
        // SurveysController.Update; computed columns (TVD / N / E / DLS /
        // …) are owned by the post-save auto-calc and would be silently
        // overwritten anyway. The EditTemplates above render those
        // columns read-only when IsTieOn is false so the user can't
        // accidentally edit them in the first place.
        var dto = new UpdateSurveyDto(
            Depth:       row.Depth,
            Inclination: row.Inclination,
            Azimuth:     row.Azimuth,
            RowVersion:  row.RowVersion);

        return await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys/{row.Id}",
            dto);
    }

    /// <summary>
    /// Tie-on cell auto-save. Wired to <c>@onfocusout</c> on each
    /// tie-on EditTemplate's <c>UnitInput</c> wrapper. On every cell
    /// blur:
    /// <list type="number">
    ///   <item>Debounce 200 ms — a rapid Tab-through across the
    ///     seven editable tie-on cells collapses to one save once
    ///     typing settles.</item>
    ///   <item>Cancel any earlier pending save in the same debounce
    ///     window via <see cref="_tieOnSaveCts"/>.</item>
    ///   <item>Serialise the actual PUT through
    ///     <see cref="_tieOnSaveSemaphore"/> so a slow save can't be
    ///     overtaken by a quicker one queued behind it (which would
    ///     PUT with the pre-recalc RowVersion and 409).</item>
    ///   <item>After save success, re-fetch tie-ons (for the bumped
    ///     RowVersion) and surveys (for the recomputed trajectory)
    ///     and rebuild <c>_rows</c> with the in-edit tie-on row
    ///     reference KEPT INTACT — Syncfusion sees no row-identity
    ///     change on the editing row and keeps the editor open. Only
    ///     the survey rows below visibly update.</item>
    /// </list>
    /// Survey rows wired through the same handler short-circuit on
    /// the IsTieOn check; their save flow stays on the explicit
    /// Update / Enter path via <see cref="OnActionCompleteAsync"/>.
    /// </summary>
    private async Task TieOnCellBlurAsync(GridRow row, bool endEditAfterSave = false)
    {
        if (!row.IsTieOn) return;

        // Dirty check against the last-known server state — a clean
        // Tab-through that didn't change any value shouldn't kick off
        // a recalc PUT. _tieOn refreshes after every successful save,
        // so it tracks the server's truth between edits; row.X is what
        // the @bind-SiValue is writing as the user types.
        if (_tieOn is null || !TieOnRowDiffersFrom(row, _tieOn)) return;

        _tieOnSaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _tieOnSaveCts = cts;

        try
        {
            await Task.Delay(200, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A fresher blur came in while we were waiting; let that
            // one drive the save instead.
            return;
        }

        await _tieOnSaveSemaphore.WaitAsync();
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var saveResult = await SaveTieOnAsync(client, row);

            if (!saveResult.IsSuccess)
            {
                _saveStatus     = saveResult.Error.AsAlertText();
                _saveAlertClass = "alert-danger";
                StateHasChanged();
                return;
            }

            // Re-fetch tie-ons (for the bumped RowVersion) and surveys
            // (for the recomputed trajectory). The tie-on row reference
            // we hand back to _rows is the SAME one the user is
            // editing, so Syncfusion sees no row-identity change and
            // keeps the editor open — only the survey rows below
            // visibly ripple to reflect the recalc.
            var tieOnsTask = client.GetAsync<List<TieOnSummaryDto>>(
                $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons");
            var surveysTask = client.GetAsync<PagedResult<SurveySummaryDto>>(
                $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");
            await Task.WhenAll(tieOnsTask, surveysTask);

            if (tieOnsTask.Result.IsSuccess)
            {
                _tieOn = tieOnsTask.Result.Value.OrderBy(t => t.Id).FirstOrDefault();
                // Update the in-edit row's RowVersion so the next
                // debounced save passes the optimistic-concurrency
                // check with the bumped server-side token.
                if (_tieOn is not null) row.RowVersion = _tieOn.RowVersion;
            }

            if (surveysTask.Result.IsSuccess)
            {
                var paged = surveysTask.Result.Value;
                _surveysTotal   = paged.Total;
                _surveysHasMore = paged.HasMore;
                _surveyCount    = _surveysTotal;
                var surveys = paged.Items;
                _minDepth = surveys.Count == 0 ? 0 : surveys.Min(s => s.Depth);
                _maxDepth = surveys.Count == 0 ? 0 : surveys.Max(s => s.Depth);

                // Mutate the survey rows in place so the recomputed
                // trajectory columns reflect on the same row instances
                // — keeps both the _rows list reference AND the tie-on
                // row reference (_rows[0]) intact, which is what
                // Syncfusion needs to keep the open editor anchored on
                // the row the user is typing in. Tie-on edits don't
                // add or remove surveys, so we don't need to touch the
                // row count or list reference.
                if (_rows is { Count: > 0 })
                {
                    var startIdx = 1; // skip the tie-on row
                    var pairCount = Math.Min(surveys.Count, _rows.Count - startIdx);
                    for (var i = 0; i < pairCount; i++)
                    {
                        UpdateSurveyRowInPlace(_rows[startIdx + i], surveys[i]);
                    }
                }
            }

            StateHasChanged();

            // Ask Syncfusion to repaint visible cells after the
            // in-place row mutation. Refresh() rerenders rows from
            // the (unchanged-reference) data source without disturbing
            // the open editor on the in-edit tie-on row.
            if (_grid is not null)
            {
                try { await _grid.Refresh(); }
                catch { /* a transient interop hiccup mid-edit shouldn't break the save flow */ }
            }

            // Caller from the LAST editable tie-on cell (Easting) signals
            // that this blur was the user tabbing out of the row entirely
            // — after the save lands, ask Syncfusion to close the edit.
            // Other cell blurs leave the row open so the user can keep
            // typing in the next cell.
            if (endEditAfterSave && _grid is not null)
            {
                try { await _grid.EndEditAsync(); }
                catch { /* same as Refresh above — non-fatal mid-edit */ }
            }
        }
        finally
        {
            _tieOnSaveSemaphore.Release();
        }
    }

    /// <summary>
    /// True if any of the seven editable tie-on fields on the in-edit
    /// row differs from the last-known server state. Used by
    /// <see cref="TieOnCellBlurAsync"/> to short-circuit a no-op blur
    /// (Tab-through without typing) before kicking off the debounce
    /// timer. Note the field-name mapping: the grid column "TVD" binds
    /// row.VerticalDepth which maps to TieOnSummaryDto.VerticalReference,
    /// and "Sub-sea" binds row.SubSea which maps to SubSeaReference —
    /// same shape as <see cref="MapTieOn"/>.
    /// </summary>
    private static bool TieOnRowDiffersFrom(GridRow row, TieOnSummaryDto t)
        => row.Depth         != t.Depth
        || row.Inclination   != t.Inclination
        || row.Azimuth       != t.Azimuth
        || row.VerticalDepth != t.VerticalReference
        || row.SubSea        != t.SubSeaReference
        || row.Northing      != t.Northing
        || row.Easting       != t.Easting;

    /// <summary>
    /// Mutate an existing survey <see cref="GridRow"/>'s properties
    /// from a freshly-fetched <see cref="SurveySummaryDto"/>. Used by
    /// <see cref="TieOnCellBlurAsync"/> to refresh the post-recalc
    /// trajectory columns on the existing survey rows without
    /// replacing their row references — Syncfusion sees the same row
    /// identities and the in-edit tie-on row at index 0 keeps its
    /// open editor.
    /// </summary>
    private static void UpdateSurveyRowInPlace(GridRow target, SurveySummaryDto src)
    {
        target.Depth           = src.Depth;
        target.Inclination     = src.Inclination;
        target.Azimuth         = src.Azimuth;
        target.VerticalDepth   = src.VerticalDepth;
        target.SubSea          = src.SubSea;
        target.North           = src.North;
        target.East            = src.East;
        target.DoglegSeverity  = src.DoglegSeverity;
        target.VerticalSection = src.VerticalSection;
        target.Northing        = src.Northing;
        target.Easting         = src.Easting;
        target.Build           = src.Build;
        target.Turn            = src.Turn;
        target.RowVersion      = src.RowVersion;
    }

    private async Task CreateDefaultTieOnAsync()
    {
        _busy = true;
        StateHasChanged();
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons",
                new CreateTieOnDto(Depth: 0, Inclination: 0, Azimuth: 0));

            if (result.IsSuccess)
            {
                // Re-fetch in place (see save handler above for the rationale).
                await LoadAsync();
                StateHasChanged();
                return;
            }
            _saveStatus     = result.Error.AsAlertText();
            _saveAlertClass = "alert-danger";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Tie-on → grid-row projection. The grid's column headers map onto
    /// tie-on fields where the data exists (TVD = VerticalReference,
    /// Sub-sea = SubSeaReference) and 0 where it doesn't (anchor's local
    /// North/East and any rate-of-change quantity).
    /// </summary>
    private static GridRow MapTieOn(TieOnSummaryDto t) => new()
    {
        RowKey          = $"T{t.Id}",
        IsTieOn         = true,
        Id              = t.Id,
        Depth           = t.Depth,
        Inclination     = t.Inclination,
        Azimuth         = t.Azimuth,
        VerticalDepth   = t.VerticalReference,
        SubSea          = t.SubSeaReference,
        North           = 0,
        East            = 0,
        DoglegSeverity  = 0,
        VerticalSection = 0,
        Northing        = t.Northing,
        Easting         = t.Easting,
        Build           = 0,
        Turn            = 0,
        RowVersion      = t.RowVersion,
    };

    private static GridRow MapSurvey(SurveySummaryDto s) => new()
    {
        RowKey          = $"S{s.Id}",
        IsTieOn         = false,
        Id              = s.Id,
        Depth           = s.Depth,
        Inclination     = s.Inclination,
        Azimuth         = s.Azimuth,
        VerticalDepth   = s.VerticalDepth,
        SubSea          = s.SubSea,
        North           = s.North,
        East            = s.East,
        DoglegSeverity  = s.DoglegSeverity,
        VerticalSection = s.VerticalSection,
        Northing        = s.Northing,
        Easting         = s.Easting,
        Build           = s.Build,
        Turn            = s.Turn,
        RowVersion      = s.RowVersion,
    };

    /// <summary>
    /// Unified row shape for the surveys grid — tie-on and survey rows
    /// share the same column set. <see cref="IsTieOn"/> drives the
    /// row-level edit-cancel in <see cref="OnActionBegin"/>;
    /// <see cref="RowKey"/> is Syncfusion's primary key (both the tie-on
    /// and the surveys carry int <see cref="Id"/> values from different
    /// tables, so the prefix avoids collisions).
    ///
    /// Settable rather than init-only because Syncfusion's edit
    /// machinery — and our own <c>&lt;UnitInput @bind-SiValue=...&gt;</c>
    /// inside <c>&lt;EditTemplate&gt;</c> — write into the row in place
    /// while the user is editing, then OnActionCompleteAsync reads the
    /// updated values back to build the UpdateTieOnDto.
    /// </summary>
    public sealed class GridRow
    {
        public string  RowKey         { get; set; } = "";
        public bool    IsTieOn        { get; set; }
        public int     Id             { get; set; }
        public double  Depth          { get; set; }
        public double  Inclination    { get; set; }
        public double  Azimuth        { get; set; }
        public double  VerticalDepth  { get; set; }
        public double  SubSea         { get; set; }
        public double  North          { get; set; }
        public double  East           { get; set; }
        public double  DoglegSeverity { get; set; }
        public double  VerticalSection{ get; set; }
        public double  Northing       { get; set; }
        public double  Easting        { get; set; }
        public double  Build          { get; set; }
        public double  Turn           { get; set; }

        /// <summary>
        /// Optimistic-concurrency token for the underlying entity (tie-on
        /// or survey). Round-tripped on inline-edit save so a concurrent
        /// edit of the same row by another user 409s rather than silently
        /// overwriting.
        /// </summary>
        public string? RowVersion     { get; set; }
    }
}
