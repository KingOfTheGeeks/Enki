using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells.Tubulars;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/tubulars/new")]
[Authorize]
public partial class TubularCreate : ComponentBase
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
        Form ??= new CreateForm { Type = "Casing" };
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobResult = await client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        _units = await UnitPrefs.ResolveAsync(jobResult.IsSuccess ? jobResult.Value.UnitSystem : null);
        // Fallback to SI on failure; the form still renders, just with raw SI inputs.
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateTubularDto(
            Type:         Form.Type,
            Order:        Form.Order,
            FromMeasured: Form.FromMeasured,
            ToMeasured:   Form.ToMeasured,
            Diameter:     Form.Diameter,
            Weight:       Form.Weight,
            Name:         string.IsNullOrWhiteSpace(Form.Name) ? null : Form.Name);

        var result = await client.PostAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tubulars");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    public sealed class CreateForm
    {
        [Required(ErrorMessage = "Tubular type is required.")]
        public string Type { get; set; } = "Casing";

        [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Order is required.")]
        public int Order { get; set; }

        [Required(ErrorMessage = "From MD is required.")]
        public double FromMeasured { get; set; }

        [Required(ErrorMessage = "To MD is required.")]
        public double ToMeasured { get; set; }

        [Required(ErrorMessage = "Diameter is required.")]
        public double Diameter { get; set; }

        [Required(ErrorMessage = "Weight is required.")]
        public double Weight { get; set; }
    }
}
