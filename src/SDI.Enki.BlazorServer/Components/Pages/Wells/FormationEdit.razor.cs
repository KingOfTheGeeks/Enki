using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells.Formations;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/formations/{FormationId:int}/edit")]
[Authorize]
public partial class FormationEdit : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode  { get; set; } = "";
    [Parameter] public Guid   JobId       { get; set; }
    [Parameter] public int    WellId      { get; set; }
    [Parameter] public int    FormationId { get; set; }

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
        var dtoTask = client.GetAsync<FormationDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations/{FormationId}");

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
            Name         = dto.Name,
            Description  = dto.Description,
            FromMeasured = dto.FromMeasured,
            ToMeasured   = dto.ToMeasured,
            Resistance   = dto.Resistance,
            RowVersion   = dto.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateFormationDto(
            Name:         Form.Name,
            FromMeasured: Form.FromMeasured,
            ToMeasured:   Form.ToMeasured,
            Resistance:   Form.Resistance,
            Description:  string.IsNullOrWhiteSpace(Form.Description) ? null : Form.Description,
            RowVersion:   Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations/{FormationId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations");
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
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations/{FormationId}");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/formations");
            return;
        }

        _error = result.Error.AsAlertText();
        _deleteArmed = false;
    }

    public sealed class EditModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(200)] public string Name { get; set; } = "";
        public string? Description { get; set; }
        [Required] public double FromMeasured { get; set; }
        [Required] public double ToMeasured { get; set; }
        [Required] public double Resistance { get; set; }
        public string? RowVersion { get; set; }
    }
}
