using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class CsvExportButton : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;

    [Parameter, EditorRequired] public string ClientName   { get; set; } = "";
    [Parameter, EditorRequired] public string Path         { get; set; } = "";
    [Parameter, EditorRequired] public string DownloadName { get; set; } = "";

    private bool _busy;
    private string? _error;

    private async Task DownloadAsync()
    {
        if (_busy) return;
        _busy = true;
        _error = null;

        try
        {
            var client = HttpClientFactory.CreateClient(ClientName);

            // Plain GET — the controller's [Produces("text/csv")]
            // attribute handles the content-type. We don't use the
            // shared HttpClientApiExtensions wrapper because that
            // assumes JSON; CSV needs the raw bytes.
            using var response = await client.GetAsync(Path);
            if (!response.IsSuccessStatusCode)
            {
                _error = $"Export failed: HTTP {(int)response.StatusCode}.";
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            await JS.InvokeVoidAsync("enkiDownloads.fromStream", DownloadName, streamRef);
        }
        catch (Exception ex)
        {
            _error = $"Export failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }
}
