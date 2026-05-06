using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Surveys;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/surveys/{SurveyId:int}/edit")]
[Authorize]
public partial class SurveyEdit : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }
    [Parameter] public int    SurveyId   { get; set; }

    [SupplyParameterFromForm]
    public EditModel? Form { get; set; }

    private SurveyDetailDto? _detail;
    private string? _error;
    private bool _loadFailed;
    private bool _deleteArmed;

    /// <summary>Job's display unit-system; drives both form inputs and the read-only computed read-outs.</summary>
    private UnitSystem _units = UnitSystem.SI;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var dtoTask = client.GetAsync<SurveyDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys/{SurveyId}");

        await Task.WhenAll(jobTask, dtoTask);

        var jobResult = jobTask.Result;
        var dtoResult = dtoTask.Result;

        if (!dtoResult.IsSuccess)
        {
            if (dtoResult.Error.Kind == ApiErrorKind.NotFound) { _loadFailed = true; return; }
            _error = dtoResult.Error.AsAlertText();
            return;
        }

        _units = await UnitPrefs.ResolveAsync(jobResult.IsSuccess ? jobResult.Value.UnitSystem : null);

        _detail = dtoResult.Value;
        Form ??= new EditModel
        {
            Depth       = _detail.Depth,
            Inclination = _detail.Inclination,
            Azimuth     = _detail.Azimuth,
            RowVersion  = _detail.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateSurveyDto(
            Depth:       Form.Depth,
            Inclination: Form.Inclination,
            Azimuth:     Form.Azimuth,
            RowVersion:  Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys/{SurveyId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    private async Task ConfirmDelete()
    {
        if (!_deleteArmed) { _deleteArmed = true; return; }

        var client = HttpClientFactory.CreateClient("EnkiApi");
        // Explicit static-call form: HttpClient has an instance
        // DeleteAsync(string) that returns HttpResponseMessage, which
        // would win overload resolution against our extension.
        var result = await HttpClientApiExtensions.DeleteAsync(client,
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys/{SurveyId}");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");
            return;
        }

        _error = result.Error.AsAlertText();
        _deleteArmed = false;
    }

    public sealed class EditModel
    {
        [Required(ErrorMessage = "Depth is required.")]
        public double Depth { get; set; }

        [Required(ErrorMessage = "Inclination is required.")]
        [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
        public double Inclination { get; set; }

        [Required(ErrorMessage = "Azimuth is required.")]
        [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
        public double Azimuth { get; set; }

        /// <summary>
        /// Optimistic-concurrency token — base64-encoded RowVersion the
        /// detail load returned. Round-tripped to the server on save so
        /// EF rejects the UPDATE if anyone else mutated the row between
        /// load and submit.
        /// </summary>
        public string? RowVersion { get; set; }
    }
}
