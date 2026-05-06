using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Comments;
using SDI.Enki.Shared.Shots;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/shots/{ShotId:int}")]
[Authorize]
public partial class ShotDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }
    [Parameter] public int    ShotId     { get; set; }

    /// <summary>Mirrors ShotsController.MaxBinaryBytes. Surfaced in the UI so the user knows the cap before they pick a file.</summary>
    private const long MaxPrimaryBytes = 250 * 1024;
    /// <summary>Mirrors ShotsController.MaxGyroBinaryBytes.</summary>
    private const long MaxGyroBytes    = 10 * 1024;

    private ShotDetailDto? _shot;
    private List<CommentDto>? _comments;
    private string? _loadError;

    /// <summary>Single in-flight flag — disables every action button while an upload / save is in progress.</summary>
    private bool _busy;

    // Per-section message slots so a primary upload error doesn't
    // overwrite a gyro success and vice versa.
    private string? _primaryMessage;
    private string  _primaryAlertClass = "alert-info";
    private string? _gyroMessage;
    private string  _gyroAlertClass    = "alert-info";
    private string? _commentMessage;

    // Editable drafts — initialised from the loaded shot, kept
    // separate so Revert can walk back to the server state.
    private string _primaryConfigDraft = "";
    private string _gyroConfigDraft    = "";
    private string _commentDraft       = "";

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        await LoadShotAsync();
        await LoadCommentsAsync();
    }

    private async Task LoadShotAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<ShotDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Shot #{ShotId} not found."
                : result.Error.AsAlertText();
            return;
        }

        _shot = result.Value;
        _primaryConfigDraft = _shot.ConfigJson     ?? "";
        _gyroConfigDraft    = _shot.GyroConfigJson ?? "";
    }

    private async Task LoadCommentsAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<CommentDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/comments");
        if (result.IsSuccess) _comments = result.Value;
    }

    // ---------- binary upload / download / delete ----------

    private async Task UploadAsync(InputFileChangeEventArgs args, bool primary)
    {
        SetMessage(null, primary);
        var max = primary ? MaxPrimaryBytes : MaxGyroBytes;
        var file = args.File;
        if (file.Size > max)
        {
            SetMessage($"File exceeds the {FormatBytes(max)} cap.", primary, error: true);
            return;
        }

        _busy = true;
        try
        {
            byte[] bytes;
            await using (var stream = file.OpenReadStream(maxAllowedSize: max))
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

            var path = primary
                ? $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/binary"
                : $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/gyro-binary";

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostMultipartNoResponseAsync(path, content);

            if (result.IsSuccess)
            {
                SetMessage($"Uploaded {file.Name} ({FormatBytes(file.Size)}). Result will recompute.", primary, error: false);
                await LoadShotAsync();
            }
            else
            {
                SetMessage(result.Error.AsAlertText(), primary, error: true);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DownloadAsync(bool primary)
    {
        SetMessage(null, primary);
        _busy = true;
        try
        {
            var path = primary
                ? $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/binary"
                : $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/gyro-binary";

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.GetBytesAsync(path);
            if (!result.IsSuccess)
            {
                SetMessage(result.Error.AsAlertText(), primary, error: true);
                return;
            }

            var fileName = primary
                ? (_shot?.BinaryName     ?? $"shot-{ShotId}.bin")
                : (_shot?.GyroBinaryName ?? $"shot-{ShotId}.gyro.bin");

            // Stream the bytes through SignalR via DotNetStreamReference
            // so we don't blow the default 32 KB JS interop limit on
            // bigger files (250 KB primary).
            using var streamRef = new DotNetStreamReference(new MemoryStream(result.Value));
            await JS.InvokeVoidAsync("enkiDownloads.fromStream", fileName, streamRef);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Wraps <see cref="DeleteAsync"/> with a JS-confirm step.
    /// Razor refuses two <c>onclick</c> attributes on the same
    /// element (the inline <c>onclick="return confirm(...)"</c>
    /// trick), so confirmation lives in C# via JS interop.
    /// </summary>
    private async Task DeleteWithConfirmAsync(bool primary)
    {
        var prompt = primary
            ? "Delete the primary capture binary? Result will also be cleared."
            : "Delete the gyro capture binary? Gyro result will also be cleared.";
        var ok = await JS.InvokeAsync<bool>("confirm", prompt);
        if (!ok) return;
        await DeleteAsync(primary);
    }

    private async Task DeleteShotWithConfirmAsync()
    {
        var name = _shot?.ShotName ?? $"#{ShotId}";
        var ok = await JS.InvokeAsync<bool>("confirm",
            $"Delete shot {name}? This removes the binary, config, result, and any comments.");
        if (!ok) return;
        await DeleteShotAsync();
    }

    private async Task DeleteAsync(bool primary)
    {
        SetMessage(null, primary);
        _busy = true;
        try
        {
            var path = primary
                ? $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/binary"
                : $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/gyro-binary";

            // Static-method form so the call binds to our typed
            // ApiResult extension rather than HttpClient's built-in
            // DeleteAsync(string) which returns HttpResponseMessage —
            // same trick the Wells / Surveys pages use.
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await HttpClientApiExtensions.DeleteAsync(client, path);

            if (result.IsSuccess)
            {
                SetMessage("Deleted.", primary, error: false);
                await LoadShotAsync();
            }
            else
            {
                SetMessage(result.Error.AsAlertText(), primary, error: true);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- config save / revert ----------

    private async Task SaveConfigAsync(bool primary)
    {
        SetMessage(null, primary);
        _busy = true;
        try
        {
            var path = primary
                ? $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/config"
                : $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/gyro-config";

            var draft = primary ? _primaryConfigDraft : _gyroConfigDraft;
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PutAsync(path, draft ?? "");

            if (result.IsSuccess)
            {
                SetMessage("Config saved. Result invalidated.", primary, error: false);
                await LoadShotAsync();
            }
            else
            {
                SetMessage(result.Error.AsAlertText(), primary, error: true);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void RevertConfig(bool primary)
    {
        if (_shot is null) return;
        if (primary) _primaryConfigDraft = _shot.ConfigJson     ?? "";
        else         _gyroConfigDraft    = _shot.GyroConfigJson ?? "";
    }

    // ---------- comments ----------

    private async Task AddCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(_commentDraft)) return;
        _commentMessage = null;
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var dto = new CreateCommentDto(_commentDraft.Trim());
            var result = await client.PostAsync<CreateCommentDto, CommentDto>(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}/comments", dto);

            if (result.IsSuccess)
            {
                _commentDraft = "";
                await LoadCommentsAsync();
            }
            else
            {
                _commentMessage = result.Error.AsAlertText();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- shot delete ----------

    private async Task DeleteShotAsync()
    {
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await HttpClientApiExtensions.DeleteAsync(client,
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}");

            if (result.IsSuccess)
            {
                Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots");
            }
            else
            {
                _primaryMessage = result.Error.AsAlertText();
                _primaryAlertClass = "alert-danger";
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- helpers ----------

    private void SetMessage(string? msg, bool primary, bool error = false)
    {
        if (primary)
        {
            _primaryMessage = msg;
            _primaryAlertClass = error ? "alert-danger" : "alert-success";
        }
        else
        {
            _gyroMessage = msg;
            _gyroAlertClass = error ? "alert-danger" : "alert-success";
        }
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
}
