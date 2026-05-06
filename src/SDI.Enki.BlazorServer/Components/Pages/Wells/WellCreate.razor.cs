using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/new")]
[Authorize]
public partial class WellCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    [SupplyParameterFromForm]
    public CreateForm? Form { get; set; }

    private string? _error;

    protected override void OnInitialized() =>
        Form ??= new CreateForm { Type = "Target" };

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateWellDto(Name: Form.Name, Type: Form.Type);

        var result = await client.PostAsync<CreateWellDto, WellSummaryDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/wells/{result.Value.Id}");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    public sealed class CreateForm
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(200, ErrorMessage = "Name must be 200 chars or fewer.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Well type is required.")]
        public string Type { get; set; } = "Target";
    }
}
