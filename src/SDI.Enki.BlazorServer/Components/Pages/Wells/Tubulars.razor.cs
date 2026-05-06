using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Paging;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.Shared.Wells.Tubulars;
using Syncfusion.Blazor.Grids;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/tubulars")]
[Authorize]
public partial class Tubulars : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }

    private List<GridRow>? _rows;
    private string? _error;
    private string? _saveStatus;
    private string  _saveAlertClass = "alert-info";

    /// <summary>Job's preferred display unit-system; drives column headers and cell projections.</summary>
    private UnitSystem _units = UnitSystem.SI;

    /// <summary>
    /// True when the well has at least one survey — combined with
    /// the auto-created tie-on at depth 0, that's the minimum needed
    /// to bracket an MD for TVD interpolation (and because the
    /// server's MD-range validation rejects any Tubular interval that
    /// lies outside the tie-on/survey envelope).
    /// </summary>
    private bool _hasEnoughSurveys;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Three parallel reads — each returns an ApiResult<T> envelope
        // so a 401/403/404/5xx surfaces as a typed ApiError instead of
        // an exception. We branch on IsSuccess on each; if either
        // failed, we set _error from the first failure and bail.
        var jobTask      = client.GetAsync<JobDetailDto>($"tenants/{TenantCode}/jobs/{JobId}");
        var tubularsTask = client.GetAsync<List<TubularSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars");
        var surveysTask  = client.GetAsync<PagedResult<SurveySummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys?take=1");
        await Task.WhenAll(jobTask, tubularsTask, surveysTask);

        var jobResult      = jobTask.Result;
        var tubularsResult = tubularsTask.Result;
        var surveysResult  = surveysTask.Result;

        if (!jobResult.IsSuccess)      { _error = jobResult.Error.AsAlertText();      return; }
        if (!tubularsResult.IsSuccess) { _error = tubularsResult.Error.AsAlertText(); return; }

        _hasEnoughSurveys = surveysResult.IsSuccess && surveysResult.Value.Total >= 1;

        _units = await UnitPrefs.ResolveAsync(jobResult.Value.UnitSystem);
        _rows  = tubularsResult.Value
            .Select(t => new GridRow
            {
                Id           = t.Id,
                Order        = t.Order,
                Type         = t.Type,
                Name         = t.Name,
                FromMeasured = t.FromMeasured,
                ToMeasured   = t.ToMeasured,
                FromTvd      = t.FromTvd,
                ToTvd        = t.ToTvd,
                Diameter     = t.Diameter,
                Weight       = t.Weight,
                RowVersion   = t.RowVersion,
            })
            .ToList();
    }

    /// <summary>
    /// Save / Delete handler. Save: UpdateTubularDto carries the full
    /// update set (Type, Order, FromMeasured, ToMeasured, Diameter,
    /// Weight, Name) so no detail-fetch round-trip is needed before
    /// PUT — Summary == Update field shape. Delete: Syncfusion's
    /// native confirm dialog gates the action; we just translate
    /// args.Data.Id into the DELETE call.
    /// </summary>
    private async Task OnActionCompleteAsync(ActionEventArgs<GridRow> args)
    {
        if (args.Data is not { } row) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var path   = $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars/{row.Id}";

        if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
        {
            // Explicit static-call form: HttpClient has an instance
            // DeleteAsync(string) returning HttpResponseMessage which
            // would win overload resolution over our extension.
            var deleteResult = await HttpClientApiExtensions.DeleteAsync(client, path);
            if (deleteResult.IsSuccess)
            {
                // In-place re-fetch instead of Nav.NavigateTo(forceLoad: true)
                // — keeps scroll/selection and avoids the full page-tear
                // flash on every grid mutation.
                await LoadAsync();
                StateHasChanged();
                return;
            }

            _saveStatus     = deleteResult.Error.AsAlertText();
            _saveAlertClass = "alert-danger";
            return;
        }

        if (args.RequestType != Syncfusion.Blazor.Grids.Action.Save) return;

        var dto = new UpdateTubularDto(
            Type:         row.Type,
            Order:        row.Order,
            FromMeasured: row.FromMeasured,
            ToMeasured:   row.ToMeasured,
            Diameter:     row.Diameter,
            Weight:       row.Weight,
            Name:         row.Name,
            // Round-trip the optimistic-concurrency token from the
            // summary loaded into the grid so a concurrent edit on
            // the same tubular 409s rather than silently overwriting.
            RowVersion:   row.RowVersion);

        var result = await client.PutAsync(path, dto);

        if (result.IsSuccess)
        {
            await LoadAsync();
            StateHasChanged();
            return;
        }

        // ApiError carries a parsed title + per-field messages when the
        // WebApi returned a ValidationProblemDetails. Future polish:
        // surface FieldErrors next to the offending grid cell instead of
        // a single banner. For now the headline tells the user what
        // happened and they can re-edit.
        _saveStatus     = result.Error.AsAlertText();
        _saveAlertClass = "alert-danger";
    }

    /// <summary>
    /// Mutable row shape backing the editable grid. Mirrors
    /// <see cref="TubularSummaryDto"/> with the int <see cref="Id"/>
    /// as the Syncfusion primary key.
    /// </summary>
    public sealed class GridRow
    {
        public int     Id           { get; set; }
        public int     Order        { get; set; }
        public string  Type         { get; set; } = "";
        public string? Name         { get; set; }
        public double  FromMeasured { get; set; }
        public double  ToMeasured   { get; set; }
        /// <summary>Derived TVD from <see cref="FromMeasured"/>; null when the well has &lt; 2 surveys.</summary>
        public double? FromTvd      { get; set; }
        /// <summary>Derived TVD from <see cref="ToMeasured"/>; null when the well has &lt; 2 surveys.</summary>
        public double? ToTvd        { get; set; }
        public double  Diameter     { get; set; }
        public double  Weight       { get; set; }
        /// <summary>
        /// Carried through from the summary fetch so inline-edit
        /// commits round-trip the optimistic-concurrency token.
        /// </summary>
        public string? RowVersion   { get; set; }
    }
}
