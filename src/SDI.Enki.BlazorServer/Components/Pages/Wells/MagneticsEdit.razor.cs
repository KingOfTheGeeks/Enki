using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/magnetics/edit")]
[Authorize]
public partial class MagneticsEdit : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }

    [SupplyParameterFromForm]
    public EditModel? Form { get; set; }

    private UnitSystem _units = UnitSystem.SI;

    /// <summary>True if a per-well row already exists at load time. Drives the Clear button visibility.</summary>
    private bool _existing;

    private bool      _clearArmed;
    private ApiError? _error;
    private string?   _clearError;

    protected override async Task OnInitializedAsync()
    {
        if (Form is not null) return;   // BL0008 dance — preserve a posted form across re-render

        var client = HttpClientFactory.CreateClient("EnkiApi");

        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var magTask = client.GetAsync<MagneticsDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/magnetics");

        await Task.WhenAll(jobTask, magTask);

        var jobResult = jobTask.Result;
        var magResult = magTask.Result;

        _units = await UnitPrefs.ResolveAsync(jobResult.IsSuccess ? jobResult.Value.UnitSystem : null);

        if (magResult.IsSuccess)
        {
            _existing = true;
            Form = new EditModel
            {
                BTotal      = magResult.Value.BTotal,
                Dip         = magResult.Value.Dip,
                Declination = magResult.Value.Declination,
                RowVersion  = magResult.Value.RowVersion,
            };
            return;
        }

        if (magResult.Error.Kind != ApiErrorKind.NotFound)
        {
            // 404 is the "not set yet" path; anything else is an
            // unexpected failure. Surface it but still let the
            // user fill the form (so they're not blocked).
            _error = magResult.Error;
        }

        // No existing row: blank form.
        Form = new EditModel();
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        // RowVersion is null on the create branch (no existing
        // row); the controller ignores it there. On the update
        // branch the controller applies it for concurrency check.
        var dto = new SetMagneticsDto(Form.BTotal, Form.Dip, Form.Declination, Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/magnetics", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}");
            return;
        }

        _error = result.Error;
    }

    private async Task ConfirmClear()
    {
        if (!_clearArmed) { _clearArmed = true; return; }

        _clearError = null;
        var client = HttpClientFactory.CreateClient("EnkiApi");
        // Explicit static-call form: HttpClient has an instance
        // DeleteAsync(string) that returns HttpResponseMessage, which
        // would win overload resolution against our extension.
        var result = await HttpClientApiExtensions.DeleteAsync(client,
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/magnetics");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}");
            return;
        }

        _clearError = result.Error.AsAlertText();
        _clearArmed = false;
    }

    public sealed class EditModel
    {
        [Required(ErrorMessage = "Declination is required.")]
        [Range(-180d, 180d, ErrorMessage = "Declination must be between −180° and 180°.")]
        public double Declination { get; set; }

        [Required(ErrorMessage = "Dip is required.")]
        [Range(-90d, 90d, ErrorMessage = "Dip must be between −90° and 90°.")]
        public double Dip { get; set; }

        [Required(ErrorMessage = "Total field is required.")]
        [Range(0d, 100_000d, ErrorMessage = "Total field is in nT and must be between 0 and 100,000.")]
        public double BTotal { get; set; }

        /// <summary>
        /// Optimistic-concurrency token from the existing-row load.
        /// Null when the well has no magnetic reference yet (create
        /// branch); the controller ignores it on create.
        /// </summary>
        public string? RowVersion { get; set; }
    }
}
