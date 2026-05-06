using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin.Audit;

[Route("/admin/audit/identity/{EntityType}/{EntityId}")]
[Layout(typeof(AdminLayout))]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class IdentityAuditEntity : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Parameter] public string EntityType { get; set; } = "";
    [Parameter] public string EntityId   { get; set; } = "";

    private IReadOnlyList<AuditLogEntryDto>? _rows;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiIdentity");
        var path = $"admin/audit/identity/{Uri.EscapeDataString(EntityType)}/{Uri.EscapeDataString(EntityId)}?take=500";

        var result = await client.GetAsync<PagedResult<AuditLogEntryDto>>(path);
        if (!result.IsSuccess)
        {
            _error = result.Error.AsAlertText();
            _rows = [];
            return;
        }

        _rows = result.Value.Items;
    }
}
