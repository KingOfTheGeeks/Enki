using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Shared.Runs;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}")]
[Authorize]
public partial class RunDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }

    /// <summary>Mirrors RunsController.MaxPassiveBinaryBytes.</summary>
    private const long MaxPassiveBytes = 250 * 1024;

    private RunDetailDto? _run;
    private string? _error;
    private bool _busy;

    // Passive section state — separate from the lifecycle / identity
    // surface above so a binary upload error doesn't clobber a prior
    // config-save success and vice versa.
    private string _passiveConfigDraft = "";
    private string? _passiveBinaryMessage;
    private string  _passiveBinaryAlertClass = "alert-info";
    private string? _passiveConfigMessage;
    private string  _passiveConfigAlertClass = "alert-info";

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<RunDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}");

        if (!result.IsSuccess)
        {
            _error = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Run #{ShortRunId} not found."
                : result.Error.AsAlertText();
            return;
        }

        _run = result.Value;
        _passiveConfigDraft = _run.PassiveConfigJson ?? "";
    }

    // ---------- passive binary ----------

    private async Task UploadPassiveBinaryAsync(InputFileChangeEventArgs args)
    {
        _passiveBinaryMessage = null;
        var file = args.File;
        if (file.Size > MaxPassiveBytes)
        {
            _passiveBinaryMessage = $"File exceeds the {FormatBytes(MaxPassiveBytes)} cap.";
            _passiveBinaryAlertClass = "alert-danger";
            return;
        }

        _busy = true;
        try
        {
            byte[] bytes;
            await using (var stream = file.OpenReadStream(maxAllowedSize: MaxPassiveBytes))
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
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/passive/binary", content);

            if (result.IsSuccess)
            {
                _passiveBinaryMessage = $"Uploaded {file.Name} ({FormatBytes(file.Size)}). Result will recompute.";
                _passiveBinaryAlertClass = "alert-success";
                await LoadAsync();
            }
            else
            {
                _passiveBinaryMessage = result.Error.AsAlertText();
                _passiveBinaryAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DownloadPassiveBinaryAsync()
    {
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.GetBytesAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/passive/binary");
            if (!result.IsSuccess)
            {
                _passiveBinaryMessage = result.Error.AsAlertText();
                _passiveBinaryAlertClass = "alert-danger";
                return;
            }

            var fileName = _run?.PassiveBinaryName ?? $"run-{RunId:N}.passive.bin";
            using var streamRef = new DotNetStreamReference(new MemoryStream(result.Value));
            await JS.InvokeVoidAsync("enkiDownloads.fromStream", fileName, streamRef);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>JS-confirm wrapper for the passive-binary delete button.</summary>
    private async Task DeletePassiveBinaryWithConfirmAsync()
    {
        var ok = await JS.InvokeAsync<bool>("confirm",
            "Delete the passive capture binary? Result will also be cleared.");
        if (!ok) return;
        await DeletePassiveBinaryAsync();
    }

    private async Task DeletePassiveBinaryAsync()
    {
        _busy = true;
        try
        {
            // Static-method form binds to our typed ApiResult
            // extension rather than HttpClient.DeleteAsync(string).
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await HttpClientApiExtensions.DeleteAsync(client,
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/passive/binary");
            if (result.IsSuccess)
            {
                await LoadAsync();
            }
            else
            {
                _passiveBinaryMessage = result.Error.AsAlertText();
                _passiveBinaryAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- passive config ----------

    private async Task SavePassiveConfigAsync()
    {
        _busy = true;
        _passiveConfigMessage = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PutAsync(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/passive/config",
                _passiveConfigDraft ?? "");
            if (result.IsSuccess)
            {
                _passiveConfigMessage = "Config saved. Result invalidated.";
                _passiveConfigAlertClass = "alert-success";
                await LoadAsync();
            }
            else
            {
                _passiveConfigMessage = result.Error.AsAlertText();
                _passiveConfigAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void RevertPassiveConfig()
    {
        _passiveConfigDraft = _run?.PassiveConfigJson ?? "";
    }

    private static string ResultClass(string? status) => status switch
    {
        "Pending"   => "enki-status-draft",
        "Computing" => "enki-status-inactive",
        "Success"   => "enki-status-active",
        "Failed"    => "enki-status-archived",
        _           => "",
    };

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024            => $"{bytes} B",
            < 1024 * 1024     => $"{bytes / 1024:N0} KB",
            _                 => $"{bytes / 1024d / 1024d:N1} MB",
        };

    private static RunStatus? ParseStatus(string s) =>
        RunStatus.TryFromName(s, out var status) ? status : null;

    private static string TransitionAction(RunStatus target) => target.Name switch
    {
        "Active"    => "start",
        "Suspended" => "suspend",
        "Completed" => "complete",
        "Cancelled" => "cancel",
        _           => target.Name.ToLowerInvariant(),
    };

    private static string TransitionLabel(RunStatus target) => target.Name switch
    {
        "Active"    => "Start",
        "Suspended" => "Suspend",
        "Completed" => "Complete",
        "Cancelled" => "Cancel",
        _           => target.Name,
    };

    private static string TransitionButtonClass(RunStatus target) => target.Name switch
    {
        // Primary visual weight on the "advance" actions; muted on terminal
        // ones because they're irreversible.
        "Active"    => "enki-button-primary",
        "Completed" => "enki-button-primary",
        _           => "",
    };

    private static string? TransitionConfirm(RunStatus target, string runName) => target.Name switch
    {
        "Cancelled" => $"return confirm('Cancel run \\'{runName}\\'? Cancelled runs are read-only.');",
        "Completed" => $"return confirm('Mark run \\'{runName}\\' as Completed? Completed runs are read-only.');",
        _           => null,
    };

    private static string TypeClass(string s) => s switch
    {
        "Gradient" => "enki-status-active",
        "Rotary"   => "enki-status-inactive",
        "Passive"  => "enki-status-draft",
        _          => "",
    };

    private static string StatusClass(string s) => s switch
    {
        "Planned"   => "enki-status-draft",
        "Active"    => "enki-status-active",
        "Suspended" => "enki-status-inactive",
        "Completed" => "enki-status-archived",
        "Cancelled" => "enki-status-archived",
        _           => "",
    };
}
