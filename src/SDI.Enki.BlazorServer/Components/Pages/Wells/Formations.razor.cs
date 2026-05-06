using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Paging;
using SDI.Enki.Shared.Surveys;
using SDI.Enki.Shared.Wells.Formations;
using Syncfusion.Blazor.Grids;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/formations")]
[Authorize]
public partial class Formations : ComponentBase
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
    /// to bracket an MD for TVD interpolation. Drives the
    /// New-formation button's disabled state and the in-page warning.
    /// </summary>
    private bool _hasEnoughSurveys;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var formationsTask = client.GetAsync<List<FormationSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations");
        // ?take=1 is enough — we only need the .Total field. The DB
        // skips fetching the rest, and we don't materialise items we
        // don't read.
        var surveysTask = client.GetAsync<PagedResult<SurveySummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys?take=1");

        await Task.WhenAll(jobTask, formationsTask, surveysTask);

        var jobResult        = jobTask.Result;
        var formationsResult = formationsTask.Result;
        var surveysResult    = surveysTask.Result;

        if (!jobResult.IsSuccess)        { _error = jobResult.Error.AsAlertText();        return; }
        if (!formationsResult.IsSuccess) { _error = formationsResult.Error.AsAlertText(); return; }

        // Surveys 404 (well unknown) would have already failed on the
        // formations call — still guard for the rare case where one
        // succeeds and the other doesn't.
        _hasEnoughSurveys = surveysResult.IsSuccess && surveysResult.Value.Total >= 1;

        _units = await UnitPrefs.ResolveAsync(jobResult.Value.UnitSystem);
        _rows  = formationsResult.Value
            .Select(f => new GridRow
            {
                Id            = f.Id,
                Name          = f.Name,
                FromMeasured  = f.FromMeasured,
                ToMeasured    = f.ToMeasured,
                FromTvd       = f.FromTvd,
                ToTvd         = f.ToTvd,
                Resistance    = f.Resistance,
                RowVersion    = f.RowVersion,
            })
            .ToList();
    }

    /// <summary>
    /// Save / Delete handler. Save: fetches FormationDetailDto first
    /// to preserve the Description that the inline grid doesn't show,
    /// then PUTs the merged <see cref="UpdateFormationDto"/>. Delete:
    /// Syncfusion's native confirm dialog gates the action; we
    /// translate args.Data.Id into the DELETE call. On either success,
    /// force-reload so the grid renders the saved state from a fresh
    /// GET.
    /// </summary>
    private async Task OnActionCompleteAsync(ActionEventArgs<GridRow> args)
    {
        if (args.Data is not { } row) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var path   = $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations/{row.Id}";

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

        // Round-trip the existing detail so we preserve fields the
        // inline grid doesn't show — Description today, anything
        // else added to FormationDetailDto in the future.
        var detailResult = await client.GetAsync<FormationDetailDto>(path);
        if (!detailResult.IsSuccess)
        {
            _saveStatus     = detailResult.Error.AsAlertText();
            _saveAlertClass = "alert-danger";
            return;
        }
        var preservedDescription = detailResult.Value.Description;

        // Use the RowVersion captured at LoadAsync time, NOT the
        // just-fetched detailResult.Value.RowVersion — the staleness
        // check needs to run against the user's last-seen state, not
        // the inline-grid's just-now-fetched state. Otherwise a
        // racing edit between the user opening the grid and saving
        // would slip past concurrency check.
        var dto = new UpdateFormationDto(
            Name:         row.Name,
            FromMeasured: row.FromMeasured,
            ToMeasured:   row.ToMeasured,
            Resistance:   row.Resistance,
            Description:  preservedDescription,
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

    /// <summary>
    /// Mutable row shape backing the editable grid. Mirrors the
    /// shape of <see cref="FormationSummaryDto"/> plus the int
    /// <see cref="Id"/> as the Syncfusion primary key. Description
    /// is deliberately not in this shape — the grid doesn't show
    /// it; the save handler fetches detail to preserve it.
    /// </summary>
    public sealed class GridRow
    {
        public int    Id           { get; set; }
        public string Name         { get; set; } = "";
        public double FromMeasured { get; set; }
        public double ToMeasured   { get; set; }
        /// <summary>Derived TVD from <see cref="FromMeasured"/>; null when the well has &lt; 2 surveys.</summary>
        public double? FromTvd     { get; set; }
        /// <summary>Derived TVD from <see cref="ToMeasured"/>; null when the well has &lt; 2 surveys.</summary>
        public double? ToTvd       { get; set; }
        public double Resistance   { get; set; }
        /// <summary>
        /// Captured at LoadAsync from the summary; passed back to
        /// the server on save for optimistic-concurrency check.
        /// </summary>
        public string? RowVersion  { get; set; }
    }
}
