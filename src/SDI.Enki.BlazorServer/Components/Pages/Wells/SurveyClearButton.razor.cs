using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

public partial class SurveyClearButton : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Parameter, EditorRequired] public string TenantCode { get; set; } = "";
    [Parameter, EditorRequired] public Guid   JobId      { get; set; }
    [Parameter, EditorRequired] public int    WellId     { get; set; }

    /// <summary>
    /// Parent-supplied callback that re-fetches the surveys + tie-on
    /// payload in place. Invoked on successful clear so the grid drops
    /// to its empty state without a full page reload — the old
    /// <c>Nav.NavigateTo(forceLoad: true)</c> shape produced a visible
    /// page-tear flash that this avoids.
    /// </summary>
    [Parameter] public EventCallback OnReload { get; set; }

    private bool _armed;
    private bool _clearing;
    private string? _message;
    private string _alertClass = "alert-info";
    private ApiError? _error;

    private string ButtonLabel =>
        _clearing ? "Clearing…"
        : _armed  ? "Click again to confirm clear"
        :           "Clear surveys";

    private async Task HandleClick()
    {
        if (!_armed)
        {
            // First click — arm the button. A real second click within
            // the user's attention window does the deed; if they
            // navigate away or refresh, the page reload disarms it.
            _armed = true;
            _message = null;
            return;
        }

        _clearing = true;
        _message  = null;
        _error    = null;
        StateHasChanged();

        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            // Explicit static-call form: HttpClient has an instance
            // DeleteAsync(string) that returns HttpResponseMessage, which
            // would win overload resolution against our extension.
            var result = await HttpClientApiExtensions.DeleteAsync(client,
                $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys");

            if (result.IsSuccess)
            {
                // Ask the parent (Surveys.razor) to re-fetch in place so
                // the grid drops to its empty state without the full page
                // reload that the old Nav.NavigateTo(forceLoad: true)
                // triggered. Avoids the page-tear flash on clear.
                await OnReload.InvokeAsync();
                return;
            }

            _error = result.Error;
        }
        finally
        {
            _clearing = false;
            _armed = false;
        }
    }
}
