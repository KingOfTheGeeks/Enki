using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}/edit")]
[Authorize]
public partial class WellEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }

    [SupplyParameterFromForm]
    public EditModel? Form { get; set; }

    private string? _loadError;
    private string? _submitError;
    private string? _deleteError;
    private bool    _deleteArmed;

    protected override async Task OnInitializedAsync()
    {
        if (Form is not null) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<WellDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Well {WellId} not found."
                : result.Error.AsAlertText();
            return;
        }

        Form = new EditModel
        {
            Name       = result.Value.Name,
            Type       = result.Value.Type,
            RowVersion = result.Value.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateWellDto(Name: Form.Name, Type: Form.Type, RowVersion: Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}");
            return;
        }

        _submitError = result.Error.AsAlertText();
    }

    private async Task ConfirmDelete()
    {
        if (!_deleteArmed) { _deleteArmed = true; return; }

        _deleteError = null;
        var client = HttpClientFactory.CreateClient("EnkiApi");
        // Explicit static-call form: HttpClient has an instance
        // DeleteAsync(string) that returns HttpResponseMessage, which
        // would win overload resolution against our extension.
        var result = await HttpClientApiExtensions.DeleteAsync(client,
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}");

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells");
            return;
        }

        _deleteError = result.Error.AsAlertText();
        _deleteArmed = false;
    }

    public sealed class EditModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(200, ErrorMessage = "Name must be 200 chars or fewer.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Well type is required.")]
        public string Type { get; set; } = "Target";

        public string? RowVersion { get; set; }
    }
}
