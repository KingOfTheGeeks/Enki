using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Editors;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Shots;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/shots/new")]
[Authorize]
public partial class ShotCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }

    /// <summary>Mirrors ShotsController.MaxBinaryBytes.</summary>
    private const long MaxBinaryBytes = 250 * 1024;

    public CreateFormModel? Form { get; set; }

    /// <summary>Buffered binary — held in memory until submit.</summary>
    private BufferedFile? _buffered;

    /// <summary>Run type so we can render the right typed editor and label
    /// the toggles. Null until OnInitializedAsync completes.</summary>
    private string? _runType;

    private bool _busy;
    private string? _pickError;
    private string? _submitError;

    /// <summary>Optional-Configuration toggle + editor refs by run type.
    /// Only one of (gradient, rotary) is meaningful at a time —
    /// determined by <see cref="_runType"/>.</summary>
    private bool _includeConfig;
    private GradientConfigurationEditor?       _gradientConfigEditor;
    private RotatingDipoleConfigurationEditor? _rotaryConfigEditor;

    private bool _includeSolution;
    private GradientSolutionEditor?       _gradientSolutionEditor;
    private RotatingDipoleSolutionEditor? _rotarySolutionEditor;

    private string ShortRunId => RunId.ToString("N")[..8];

    /// <summary>User-facing name of the configuration shape — tracks
    /// Marduk's class names so the field labels match the underlying
    /// data shape exactly.</summary>
    private string ConfigShapeName => _runType switch
    {
        "Gradient" => "GradientConfiguration",
        "Rotary"   => "RotatingDipoleConfiguration",
        "Passive"  => "PassiveConfiguration",
        _          => "Configuration",
    };

    private string SolutionShapeName => _runType switch
    {
        "Gradient" => "GradientSolution",
        "Rotary"   => "RotatingDipoleSolution",
        "Passive"  => "PassiveSolution",
        _          => "Solution",
    };

    protected override async Task OnInitializedAsync()
    {
        // Load the parent run so the editor toggles render the right
        // typed form (Gradient* vs RotatingDipole*). Failure here
        // isn't fatal — the editors fall back to a "not available"
        // hint and the upload still works.
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var runResult = await client.GetAsync<RunDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}");
        if (runResult.IsSuccess) _runType = runResult.Value?.Type;
    }

    private async Task HandleBinaryPickedAsync(InputFileChangeEventArgs args)
    {
        _pickError = null;
        var file = args.File;
        if (file.Size > MaxBinaryBytes)
        {
            _pickError = $"File exceeds the {FormatBytes(MaxBinaryBytes)} cap.";
            return;
        }
        if (file.Size == 0)
        {
            _pickError = "File is empty.";
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
            _buffered = new BufferedFile(
                FileName: file.Name,
                ContentType: string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                LastModified: file.LastModified,
                Bytes: bytes);

            // Pre-fill form fields from the file metadata. Strip the
            // extension so the shot name reads like "shot-3A" rather
            // than "shot-3A.bin"; user can edit if they want it back.
            //
            // Force Kind=Unspecified on the pre-filled DateTime —
            // file.LastModified.LocalDateTime returns Kind=Local,
            // which makes `new DateTimeOffset(d, TimeSpan.Zero)`
            // throw at submit time when the runtime's local zone
            // isn't UTC.
            Form = new CreateFormModel
            {
                ShotName = Path.GetFileNameWithoutExtension(file.Name),
                FileTime = DateTime.SpecifyKind(file.LastModified.LocalDateTime, DateTimeKind.Unspecified),
            };
        }
        finally
        {
            _busy = false;
        }
    }

    private void ClearBuffered()
    {
        _buffered          = null;
        _includeConfig     = false;
        _includeSolution   = false;
        Form               = null;
        _submitError       = null;
    }

    private async Task SubmitAsync()
    {
        if (Form is null || _buffered is null) return;
        _submitError = null;
        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");

            // Step 1 — create the shot row (identity only).
            var createDto = new CreateShotDto(
                ShotName: Form.ShotName,
                FileTime: ToOffset(Form.FileTime));

            var createResult = await client.PostAsync<CreateShotDto, ShotDetailDto>(
                $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots", createDto);

            if (!createResult.IsSuccess)
            {
                _submitError = createResult.Error.AsAlertText();
                return;
            }

            var shotId = createResult.Value?.Id ?? 0;
            if (shotId == 0)
            {
                _submitError = "Server accepted the create call but returned no shot id.";
                return;
            }

            // Step 2 — upload the binary.
            using (var content = new MultipartFormDataContent())
            {
                var body = new ByteArrayContent(_buffered.Bytes);
                body.Headers.ContentType = new MediaTypeHeaderValue(_buffered.ContentType);
                content.Add(body, "file", _buffered.FileName);

                var uploadResult = await client.PostMultipartNoResponseAsync(
                    $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}/binary",
                    content);

                if (!uploadResult.IsSuccess)
                {
                    _submitError = "Shot created but binary upload failed: "
                        + uploadResult.Error.AsAlertText();
                    Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}");
                    return;
                }
            }

            // Step 3 — optional config (typed editor → JSON).
            var configJson = SerializeConfigOrNull();
            if (configJson is not null)
            {
                var cfgResult = await client.PutAsync(
                    $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}/config",
                    configJson);
                if (!cfgResult.IsSuccess)
                {
                    _submitError = "Shot created and binary uploaded, but configuration save failed: "
                        + cfgResult.Error.AsAlertText();
                    Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}");
                    return;
                }
            }

            // Step 4 — optional solution (typed editor → JSON; sets ResultStatus=Success).
            var solutionJson = SerializeSolutionOrNull();
            if (solutionJson is not null)
            {
                var solResult = await client.PutAsync(
                    $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}/result",
                    solutionJson);
                if (!solResult.IsSuccess)
                {
                    _submitError = "Shot created and binary/config saved, but solution save failed: "
                        + solResult.Error.AsAlertText();
                    Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}");
                    return;
                }
            }

            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{shotId}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Pull JSON from whichever typed config editor is mounted, or
    /// null if no config is being attached. Run-type drives which
    /// editor was rendered; the other ref stays null and is skipped.
    /// </summary>
    private string? SerializeConfigOrNull()
    {
        if (!_includeConfig) return null;
        return _runType switch
        {
            "Gradient" => _gradientConfigEditor?.ToJson(),
            "Rotary"   => _rotaryConfigEditor?.ToJson(),
            _          => null,
        };
    }

    private string? SerializeSolutionOrNull()
    {
        if (!_includeSolution) return null;
        return _runType switch
        {
            "Gradient" => _gradientSolutionEditor?.ToJson(),
            "Rotary"   => _rotarySolutionEditor?.ToJson(),
            _          => null,
        };
    }

    /// <summary>
    /// Build a <see cref="DateTimeOffset"/> from a user-picked
    /// local <see cref="DateTime"/> without the runtime throwing
    /// "UTC offset doesn't match" — happens when the DateTime's
    /// Kind is Local and we ask for a different (TimeSpan.Zero)
    /// offset. Treat the input as wall-clock time (Unspecified)
    /// and tag with TimeSpan.Zero to match the codebase
    /// convention used elsewhere for ToOffset helpers.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime d) =>
        new(DateTime.SpecifyKind(d, DateTimeKind.Unspecified), TimeSpan.Zero);

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024            => $"{bytes} B",
            < 1024 * 1024     => $"{bytes / 1024:N0} KB",
            _                 => $"{bytes / 1024d / 1024d:N1} MB",
        };

    public sealed class CreateFormModel
    {
        [Required(ErrorMessage = "Shot name is required.")]
        [MaxLength(200, ErrorMessage = "Shot name must be 200 chars or fewer.")]
        public string ShotName { get; set; } = "";

        [Required(ErrorMessage = "File time is required.")]
        public DateTime FileTime { get; set; } = DateTime.Now;
    }

    private sealed record BufferedFile(
        string FileName,
        string ContentType,
        DateTimeOffset LastModified,
        byte[] Bytes);
}
