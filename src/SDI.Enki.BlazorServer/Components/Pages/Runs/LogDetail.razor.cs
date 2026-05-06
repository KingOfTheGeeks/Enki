using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Logs;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/logs/{LogId:int}")]
[Authorize]
public partial class LogDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }
    [Parameter] public int    LogId      { get; set; }

    /// <summary>Mirrors LogsController.MaxBinaryBytes.</summary>
    private const long MaxBinaryBytes = 250 * 1024;

    private LogDetailDto? _log;
    private string? _loadError;
    private bool _busy;

    private string _configDraft = "";

    private string? _binaryMessage;
    private string  _binaryAlertClass = "alert-info";
    private string? _configMessage;
    private string  _configAlertClass = "alert-info";
    private string? _resultMessage;
    private string  _resultAlertClass = "alert-info";

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<LogDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Log #{LogId} not found."
                : result.Error.AsAlertText();
            return;
        }
        _log = result.Value;
        _configDraft = _log.ConfigJson ?? "";
    }

    // ---------- binary ----------

    private async Task UploadBinaryAsync(InputFileChangeEventArgs args)
    {
        _binaryMessage = null;
        var file = args.File;
        if (file.Size > MaxBinaryBytes)
        {
            _binaryMessage = $"File exceeds the {FormatBytes(MaxBinaryBytes)} cap.";
            _binaryAlertClass = "alert-danger";
            return;
        }

        _busy = true;
        try
        {
            byte[] bytes;
            await using (var stream = file.OpenReadStream(maxAllowedSize: MaxBinaryBytes))
            await using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            using var content = new MultipartFormDataContent();
            var body = new ByteArrayContent(bytes);
            body.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(body, "file", file.Name);

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostMultipartNoResponseAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}/binary", content);

            if (result.IsSuccess)
            {
                _binaryMessage = $"Uploaded {file.Name} ({FormatBytes(file.Size)}).";
                _binaryAlertClass = "alert-success";
                await LoadAsync();
            }
            else
            {
                _binaryMessage = result.Error.AsAlertText();
                _binaryAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DownloadBinaryAsync()
    {
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.GetBytesAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}/binary");
            if (!result.IsSuccess)
            {
                _binaryMessage = result.Error.AsAlertText();
                _binaryAlertClass = "alert-danger";
                return;
            }

            var fileName = _log?.BinaryName ?? $"log-{LogId}.bin";
            using var streamRef = new DotNetStreamReference(new MemoryStream(result.Value));
            await JS.InvokeVoidAsync("enkiDownloads.fromStream", fileName, streamRef);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- config ----------

    private async Task SaveConfigAsync()
    {
        _busy = true;
        _configMessage = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PutAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}/config",
                _configDraft ?? "");

            if (result.IsSuccess)
            {
                _configMessage = "Config saved.";
                _configAlertClass = "alert-success";
                await LoadAsync();
            }
            else
            {
                _configMessage = result.Error.AsAlertText();
                _configAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void RevertConfig()
    {
        _configDraft = _log?.ConfigJson ?? "";
    }

    // ---------- result files ----------

    private async Task UploadResultFileAsync(InputFileChangeEventArgs args)
    {
        _resultMessage = null;
        var file = args.File;
        if (file.Size > MaxBinaryBytes)
        {
            _resultMessage = $"File exceeds the {FormatBytes(MaxBinaryBytes)} cap.";
            _resultAlertClass = "alert-danger";
            return;
        }

        _busy = true;
        try
        {
            byte[] bytes;
            await using (var stream = file.OpenReadStream(maxAllowedSize: MaxBinaryBytes))
            await using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            using var content = new MultipartFormDataContent();
            var body = new ByteArrayContent(bytes);
            body.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(body, "file", file.Name);

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostMultipartAsync<LogResultFileDto>(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}/result-files", content);

            if (result.IsSuccess)
            {
                _resultMessage = $"Attached {file.Name}.";
                _resultAlertClass = "alert-success";
                await LoadAsync();
            }
            else
            {
                _resultMessage = result.Error.AsAlertText();
                _resultAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DownloadResultFileAsync(LogResultFileDto rf)
    {
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.GetBytesAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}/result-files/{rf.Id}");
            if (!result.IsSuccess)
            {
                _resultMessage = result.Error.AsAlertText();
                _resultAlertClass = "alert-danger";
                return;
            }

            using var streamRef = new DotNetStreamReference(new MemoryStream(result.Value));
            await JS.InvokeVoidAsync("enkiDownloads.fromStream", rf.FileName, streamRef);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Wraps <see cref="DeleteResultFileAsync"/> with a JS confirm.
    /// Razor disallows two <c>onclick</c> attributes on the same
    /// element, so confirmation lives in C# via JS interop.
    /// </summary>
    private async Task DeleteResultFileWithConfirmAsync(LogResultFileDto rf)
    {
        var ok = await JS.InvokeAsync<bool>("confirm", $"Delete result file {rf.FileName}?");
        if (!ok) return;
        await DeleteResultFileAsync(rf.Id);
    }

    private async Task DeleteLogWithConfirmAsync()
    {
        var name = _log?.ShotName ?? $"#{LogId}";
        var ok = await JS.InvokeAsync<bool>("confirm",
            $"Delete log {name}? Result files will also be removed.");
        if (!ok) return;
        await DeleteLogAsync();
    }

    private async Task DeleteResultFileAsync(int fileId)
    {
        _busy = true;
        try
        {
            // Static-method form to bind to our typed ApiResult
            // extension rather than HttpClient.DeleteAsync(string).
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await HttpClientApiExtensions.DeleteAsync(client,
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}/result-files/{fileId}");
            if (result.IsSuccess)
            {
                await LoadAsync();
            }
            else
            {
                _resultMessage = result.Error.AsAlertText();
                _resultAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- log delete ----------

    private async Task DeleteLogAsync()
    {
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await HttpClientApiExtensions.DeleteAsync(client,
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}");
            if (result.IsSuccess)
            {
                Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs");
            }
            else
            {
                _binaryMessage = result.Error.AsAlertText();
                _binaryAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024            => $"{bytes} B",
            < 1024 * 1024     => $"{bytes / 1024:N0} KB",
            _                 => $"{bytes / 1024d / 1024d:N1} MB",
        };
}
