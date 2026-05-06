using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells.CommonMeasures;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/common-measures/new")]
[Authorize]
public partial class CommonMeasureCreate : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }

    [SupplyParameterFromForm]
    public CreateForm? Form { get; set; }

    private string? _error;
    private UnitSystem _units = UnitSystem.SI;

    protected override async Task OnInitializedAsync()
    {
        Form ??= new CreateForm();
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobResult = await client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        _units = await UnitPrefs.ResolveAsync(jobResult.IsSuccess ? jobResult.Value.UnitSystem : null);
        // Fallback to SI on failure; the form still renders.
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateCommonMeasureDto(
            FromMeasured: Form.FromMeasured,
            ToMeasured:   Form.ToMeasured,
            Value:        Form.Value);

        var result = await client.PostAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/common-measures");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    public sealed class CreateForm
    {
        [Required(ErrorMessage = "From MD is required.")]
        public double FromMeasured { get; set; }

        [Required(ErrorMessage = "To MD is required.")]
        public double ToMeasured { get; set; }

        [Required(ErrorMessage = "Value is required.")]
        public double Value { get; set; }
    }
}
