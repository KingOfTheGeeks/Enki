using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin.Audit;

[Route("/admin/audit")]
[Layout(typeof(AdminLayout))]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class AuditHome : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    private int? _masterTotal;
    private int? _identityTotal;
    private int? _authTotal;
    private int? _denialCount;

    private AuditLogEntryDto? _masterLatest;
    private AuditLogEntryDto? _identityLatest;
    private AuthEventEntryDto? _authLatest;

    protected override async Task OnInitializedAsync()
    {
        // 7-day window, parallel calls. Each returns top-1 plus the
        // total count via PagedResult.
        var since = DateTimeOffset.UtcNow.AddDays(-7).UtcDateTime.ToString("O");
        var apiClient      = HttpClientFactory.CreateClient("EnkiApi");
        var identityClient = HttpClientFactory.CreateClient("EnkiIdentity");

        var masterTask   = apiClient.GetAsync<PagedResult<AuditLogEntryDto>>(
            $"admin/audit/master?from={Uri.EscapeDataString(since)}&take=1");
        var identityTask = identityClient.GetAsync<PagedResult<AuditLogEntryDto>>(
            $"admin/audit/identity?from={Uri.EscapeDataString(since)}&take=1");
        var authTask     = identityClient.GetAsync<PagedResult<AuthEventEntryDto>>(
            $"admin/audit/auth-events?from={Uri.EscapeDataString(since)}&take=1");
        var denialTask   = apiClient.GetAsync<PagedResult<AuditLogEntryDto>>(
            $"admin/audit/master?entityType=AuthzDenial&from={Uri.EscapeDataString(since)}&take=1");

        await Task.WhenAll(masterTask, identityTask, authTask, denialTask);

        if (masterTask.Result.IsSuccess)
        {
            _masterTotal  = masterTask.Result.Value.Total;
            _masterLatest = masterTask.Result.Value.Items.FirstOrDefault();
        }
        if (identityTask.Result.IsSuccess)
        {
            _identityTotal  = identityTask.Result.Value.Total;
            _identityLatest = identityTask.Result.Value.Items.FirstOrDefault();
        }
        if (authTask.Result.IsSuccess)
        {
            _authTotal  = authTask.Result.Value.Total;
            _authLatest = authTask.Result.Value.Items.FirstOrDefault();
        }
        if (denialTask.Result.IsSuccess)
        {
            _denialCount = denialTask.Result.Value.Total;
        }
    }
}
