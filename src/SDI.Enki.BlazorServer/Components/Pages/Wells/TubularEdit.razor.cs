using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells.Tubulars;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/tubulars/{TubularId:int}/edit")]
[Authorize]
public partial class TubularEdit : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }
    [Parameter] public int    TubularId  { get; set; }

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
        var dtoTask = client.GetAsync<TubularDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars/{TubularId}");

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

        var dto = dtoResult.Value;
        Form ??= new EditModel
        {
            Type         = dto.Type,
            Name         = dto.Name,
            Order        = dto.Order,
            FromMeasured = dto.FromMeasured,
            ToMeasured   = dto.ToMeasured,
            Diameter     = dto.Diameter,
            Weight       = dto.Weight,
            RowVersion   = dto.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateTubularDto(
            Type:         Form.Type,
            Order:        Form.Order,
            FromMeasured: Form.FromMeasured,
            ToMeasured:   Form.ToMeasured,
            Diameter:     Form.Diameter,
            Weight:       Form.Weight,
            Name:         string.IsNullOrWhiteSpace(Form.Name) ? null : Form.Name,
            RowVersion:   Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars/{TubularId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars");
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
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars/{TubularId}");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars");
            return;
        }

        _error = result.Error.AsAlertText();
        _deleteArmed = false;
    }

    public sealed class EditModel
    {
        [Required] public string Type { get; set; } = "Casing";
        [MaxLength(200)] public string? Name { get; set; }
        [Required] public int Order { get; set; }
        [Required] public double FromMeasured { get; set; }
        [Required] public double ToMeasured { get; set; }
        [Required] public double Diameter { get; set; }
        [Required] public double Weight { get; set; }

        /// <summary>
        /// Optimistic-concurrency token — base64-encoded RowVersion.
        /// Round-tripped to the server on save; stale values 409.
        /// </summary>
        public string? RowVersion { get; set; }
    }
}
