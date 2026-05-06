using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Paging;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.Shared.Wells.CommonMeasures;
using Syncfusion.Blazor.Grids;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/common-measures")]
[Authorize]
public partial class CommonMeasures : ComponentBase
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
    private UnitSystem _units = UnitSystem.SI;

    /// <summary>
    /// True when the well has at least one survey — combined with
    /// the auto-created tie-on at depth 0, that's the minimum needed
    /// to bracket an MD for TVD interpolation. Drives the New-measure
    /// button's disabled state and the in-page warning.
    /// </summary>
    private bool _hasEnoughSurveys;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var measuresTask = client.GetAsync<List<CommonMeasureSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures");
        var surveysTask = client.GetAsync<PagedResult<SurveySummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys?take=1");

        await Task.WhenAll(jobTask, measuresTask, surveysTask);

        var jobResult      = jobTask.Result;
        var measuresResult = measuresTask.Result;
        var surveysResult  = surveysTask.Result;

        if (!jobResult.IsSuccess)      { _error = jobResult.Error.AsAlertText();      return; }
        if (!measuresResult.IsSuccess) { _error = measuresResult.Error.AsAlertText(); return; }

        _hasEnoughSurveys = surveysResult.IsSuccess && surveysResult.Value.Total >= 1;

        _units = await UnitPrefs.ResolveAsync(jobResult.Value.UnitSystem);
        _rows  = measuresResult.Value
            .Select(m => new GridRow
            {
                Id           = m.Id,
                FromMeasured = m.FromMeasured,
                ToMeasured   = m.ToMeasured,
                FromTvd      = m.FromTvd,
                ToTvd        = m.ToTvd,
                Value        = m.Value,
                RowVersion   = m.RowVersion,
            })
            .ToList();
    }

    /// <summary>
    /// Save handler. The Common Measures Summary and Update DTOs share
    /// the same field set, so no detail round-trip is needed — build
    /// the UpdateCommonMeasureDto straight from the row and PUT.
    /// </summary>
    private async Task OnActionCompleteAsync(ActionEventArgs<GridRow> args)
    {
        if (args.Data is not { } row) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var path   = $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures/{row.Id}";

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

        var dto = new UpdateCommonMeasureDto(
            FromMeasured: row.FromMeasured,
            ToMeasured:   row.ToMeasured,
            Value:        row.Value,
            RowVersion:   row.RowVersion);

        var result = await client.PutAsync(path, dto);

        if (result.IsSuccess)
        {
            await LoadAsync();
            StateHasChanged();
            return;
        }

        _saveStatus     = result.Error.AsAlertText();
        _saveAlertClass = "alert-danger";
    }

    /// <summary>Mutable row shape backing the editable grid.</summary>
    public sealed class GridRow
    {
        public int    Id           { get; set; }
        public double FromMeasured { get; set; }
        public double ToMeasured   { get; set; }
        /// <summary>Derived TVD from <see cref="FromMeasured"/>; null when the well has &lt; 2 surveys.</summary>
        public double? FromTvd     { get; set; }
        /// <summary>Derived TVD from <see cref="ToMeasured"/>; null when the well has &lt; 2 surveys.</summary>
        public double? ToTvd       { get; set; }
        public double Value        { get; set; }
        public string? RowVersion  { get; set; }
    }
}
