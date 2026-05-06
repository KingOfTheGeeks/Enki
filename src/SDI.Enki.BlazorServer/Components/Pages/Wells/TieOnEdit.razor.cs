using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells.TieOns;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/tieons/{TieOnId:int}/edit")]
[Authorize]
public partial class TieOnEdit : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }
    [Parameter] public int    TieOnId    { get; set; }

    private string? _error;
    private bool _loadFailed;
    private bool _resetArmed;
    private UnitSystem _units = UnitSystem.SI;

    [SupplyParameterFromForm]
    public EditModel? Form { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobTask = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var dtoTask = client.GetAsync<TieOnDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons/{TieOnId}");

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
            Depth                    = dto.Depth,
            Inclination              = dto.Inclination,
            Azimuth                  = dto.Azimuth,
            North                    = dto.North,
            East                     = dto.East,
            Northing                 = dto.Northing,
            Easting                  = dto.Easting,
            VerticalReference        = dto.VerticalReference,
            SubSeaReference          = dto.SubSeaReference,
            VerticalSectionDirection = dto.VerticalSectionDirection,
            RowVersion               = dto.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateTieOnDto(
            Depth:                    Form.Depth,
            Inclination:              Form.Inclination,
            Azimuth:                  Form.Azimuth,
            North:                    Form.North,
            East:                     Form.East,
            Northing:                 Form.Northing,
            Easting:                  Form.Easting,
            VerticalReference:        Form.VerticalReference,
            SubSeaReference:          Form.SubSeaReference,
            VerticalSectionDirection: Form.VerticalSectionDirection,
            RowVersion:               Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons/{TieOnId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    private async Task ConfirmReset()
    {
        if (!_resetArmed)
        {
            // First click — arm the reset; second click within the
            // same render cycle commits.
            _resetArmed = true;
            return;
        }

        var client = HttpClientFactory.CreateClient("EnkiApi");
        // Explicit static-call form: HttpClient has an instance
        // DeleteAsync(string) that returns HttpResponseMessage, which
        // would win overload resolution against our extension. The
        // server-side DELETE zeros observed + reference values but
        // keeps the row.
        var result = await HttpClientApiExtensions.DeleteAsync(client,
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/tieons/{TieOnId}");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");
            return;
        }

        _error = result.Error.AsAlertText();
        _resetArmed = false;
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

        public double North { get; set; }
        public double East { get; set; }
        public double Northing { get; set; }
        public double Easting { get; set; }
        public double VerticalReference { get; set; }
        public double SubSeaReference { get; set; }
        public double VerticalSectionDirection { get; set; }

        /// <summary>
        /// Optimistic-concurrency token — base64-encoded RowVersion.
        /// Round-tripped to the server on save; stale values 409.
        /// </summary>
        public string? RowVersion { get; set; }
    }
}
