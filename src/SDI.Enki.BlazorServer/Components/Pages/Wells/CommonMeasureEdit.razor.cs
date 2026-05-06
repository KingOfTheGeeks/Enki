using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells.CommonMeasures;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/common-measures/{MeasureId:int}/edit")]
[Authorize]
public partial class CommonMeasureEdit : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }
    [Parameter] public int    MeasureId  { get; set; }

    [SupplyParameterFromForm]
    public EditModel? Form { get; set; }

    private string? _error;
    private bool _loadFailed;
    private bool _deleteArmed;
    private UnitSystem _units = UnitSystem.SI;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var dtoTask = client.GetAsync<CommonMeasureDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures/{MeasureId}");

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

        Form ??= new EditModel
        {
            FromMeasured = dtoResult.Value.FromMeasured,
            ToMeasured   = dtoResult.Value.ToMeasured,
            Value        = dtoResult.Value.Value,
            RowVersion   = dtoResult.Value.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateCommonMeasureDto(
            FromMeasured: Form.FromMeasured,
            ToMeasured:   Form.ToMeasured,
            Value:        Form.Value,
            RowVersion:   Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures/{MeasureId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures");
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
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures/{MeasureId}");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures");
            return;
        }

        _error = result.Error.AsAlertText();
        _deleteArmed = false;
    }

    public sealed class EditModel
    {
        [Required] public double FromMeasured { get; set; }
        [Required] public double ToMeasured { get; set; }
        [Required] public double Value { get; set; }
        public string? RowVersion { get; set; }
    }
}
