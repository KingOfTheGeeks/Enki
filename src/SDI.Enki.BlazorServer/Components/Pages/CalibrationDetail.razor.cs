using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Calibrations;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/calibrations/{Id:guid}")]
[Authorize]
public partial class CalibrationDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Parameter] public Guid Id { get; set; }

    private CalibrationDetailDto? _cal;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<CalibrationDetailDto>($"calibrations/{Id}");

        if (!result.IsSuccess)
        {
            _error = result.Error.Kind == ApiErrorKind.NotFound
                ? "Calibration not found."
                : result.Error.AsAlertText();
            return;
        }
        _cal = result.Value;
    }

    private static string Dashed(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string DashClass(string? s) => string.IsNullOrWhiteSpace(s) ? "enki-dash" : "";
}
