using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Surveys;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/surveys/new")]
[Authorize]
public partial class SurveyCreate : ComponentBase
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

    /// <summary>
    /// Job's preferred display unit-system. Drives the
    /// <see cref="UnitInput"/> rendering via the surrounding cascade.
    /// Defaults to strict SI so an incomplete fetch doesn't silently
    /// project numbers as the wrong unit.
    /// </summary>
    private UnitSystem _units = UnitSystem.SI;

    protected override async Task OnInitializedAsync()
    {
        Form ??= new CreateForm();
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobResult = await client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        _units = await UnitPrefs.ResolveAsync(jobResult.IsSuccess ? jobResult.Value.UnitSystem : null);
        // _units stays at SI fallback on failure so the form still renders.
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateSurveyDto(
            Depth:       Form.Depth,
            Inclination: Form.Inclination,
            Azimuth:     Form.Azimuth);

        var result = await client.PostAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    public sealed class CreateForm
    {
        [Required(ErrorMessage = "Depth is required.")]
        public double Depth { get; set; }

        [Required(ErrorMessage = "Inclination is required.")]
        [Range(0d, 180d, ErrorMessage = "Inclination must be between 0 and 180 degrees.")]
        public double Inclination { get; set; }

        [Required(ErrorMessage = "Azimuth is required.")]
        [Range(0d, 360d, ErrorMessage = "Azimuth must be between 0 and 360 degrees.")]
        public double Azimuth { get; set; }
    }
}
