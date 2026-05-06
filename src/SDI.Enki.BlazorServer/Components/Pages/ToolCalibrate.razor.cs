using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Calibrations.Processing;

namespace SDI.Enki.BlazorServer.Components.Pages;

// Calibration runs are Office+ — calibration generates master content
// (Calibration row + 25 Shot rows under it). The Tool itself stays
// readable for everyone (ToolDetail) so Field can SEE the calibration
// history without being able to PUSH a new one.
[Route("/tools/{Serial:int}/calibrate")]
[Authorize(Policy = EnkiPolicies.CanWriteMasterContent)]
public partial class ToolCalibrate : ComponentBase, IDisposable
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;

    [Parameter] public int Serial { get; set; }

    private enum View { Upload, Preview, Results, Saved }
    private View _view = View.Upload;

    // Upload state
    private List<IBrowserFile> _picked = new();
    private string? _uploadError;
    private bool _uploading;

    // Preview state
    private Guid? _sessionId;
    private ProcessingSessionStatusDto? _status;
    private System.Threading.Timer? _pollTimer;
    private ProcessingDefaultsDto? _defaults;

    // Form state for compute (ref fields)
    private double _gTotal, _bTotal, _dipDegrees, _declinationDegrees;
    private double _coilConstant, _activeBDipDegrees, _sampleRateHz, _manualSign;
    private double _defaultCurrent;
    // Length 25, indexed 0..24 to match shot indices directly. Shot 0 is the
    // baseline (loop not energized) and is never user-toggleable; entries at
    // index 0 are unused. Compute submission slices [1..24].
    private bool[] _enabled = Enumerable.Repeat(true, 25).ToArray();
    private double[] _currents = new double[25];
    private bool _computing;
    private string? _computeError;

    // Results state
    private ProcessingResultDto? _result;
    private bool _saving;
    private string? _saveError;
    private string? _calibrationName;
    private string? _calibratedBy;
    private string? _notes;

    // Saved state
    private Guid? _savedCalibrationId;
    private Guid? _supersededCalibrationId;

    public void Dispose() => _pollTimer?.Dispose();

    // ====================== Upload ======================

    /// <summary>
    /// Fires on every InputFile change (drop or browse). Validates names,
    /// captures the IBrowserFile references in <see cref="_picked"/>, and
    /// auto-fires <see cref="StartProcessingAsync"/> as soon as the pick
    /// is a valid 25-file set — operator drops 25 files, wizard advances
    /// to step 2/3 without an extra click.
    /// </summary>
    private async Task HandleFilesPicked(InputFileChangeEventArgs args)
    {
        _uploadError = null;

        // Cap above 25 so an over-pick (e.g. 50) reaches ValidatePicked
        // with a friendly count message instead of throwing inside
        // InputFile (which silently leaves _picked empty).
        try
        {
            _picked = args.GetMultipleFiles(maximumFileCount: 100).ToList();
        }
        catch (InvalidOperationException ex)
        {
            _uploadError = $"Too many files selected: {ex.Message}";
            _picked = new();
            return;
        }

        if (!_uploading && ValidatePicked(out _))
        {
            await StartProcessingAsync();
        }
    }

    private static int NameToIndex(IBrowserFile f) =>
        int.TryParse(Path.GetFileNameWithoutExtension(f.Name), out var i) ? i : int.MaxValue;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private bool ValidatePicked(out string report)
    {
        if (_picked.Count != 25)
        {
            report = $"Need 25 files (0.bin baseline + 1..24), picked {_picked.Count}.";
            return false;
        }

        var seen = new HashSet<int>();
        foreach (var f in _picked)
        {
            var name = Path.GetFileNameWithoutExtension(f.Name);
            if (!int.TryParse(name, out var idx) || idx is < 0 or > 24)
            {
                report = $"File '{f.Name}' is not '{{0..24}}.bin'.";
                return false;
            }
            if (!seen.Add(idx))
            {
                report = $"Duplicate shot index {idx}.";
                return false;
            }
        }

        report = "All 25 shots present (0.bin baseline + 1.bin–24.bin).";
        return true;
    }

    private async Task StartProcessingAsync()
    {
        _uploading = true;
        _uploadError = null;

        try
        {
            using var content = new MultipartFormDataContent();
            foreach (var f in _picked)
            {
                // 50 MB safety cap per file; the server enforces a 200 MB total.
                var bytes = new byte[f.Size];
                using var stream = f.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
                int read = 0, off = 0;
                while ((read = await stream.ReadAsync(bytes.AsMemory(off, bytes.Length - off))) > 0)
                    off += read;

                var part = new ByteArrayContent(bytes, 0, off);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(part, "files", f.Name);
            }

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var post = await client.PostMultipartAsync<ProcessingSessionStatusDto>(
                $"tools/{Serial}/calibrations/process", content);

            if (!post.IsSuccess)
            {
                _uploadError = post.Error.AsAlertText();
                return;
            }

            _status = post.Value;
            _sessionId = _status?.SessionId;
            _view = View.Preview;

            // Load defaults in parallel with kicking off polling.
            _ = Task.Run(LoadDefaultsAsync);
            StartPolling();
        }
        catch (Exception ex)
        {
            _uploadError = $"Upload failed: {ex.Message}";
        }
        finally
        {
            _uploading = false;
        }
    }

    // ====================== Preview ======================

    private async Task LoadDefaultsAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<ProcessingDefaultsDto>("calibrations/processing-defaults");
        if (!result.IsSuccess) return;

        _defaults = result.Value;
        _gTotal             = _defaults!.GTotal;
        _bTotal             = _defaults.BTotal;
        _dipDegrees         = _defaults.DipDegrees;
        _declinationDegrees = _defaults.DeclinationDegrees;
        _coilConstant       = _defaults.CoilConstant;
        _activeBDipDegrees  = _defaults.ActiveBDipDegrees;
        _sampleRateHz       = _defaults.SampleRateHz;
        _manualSign         = _defaults.ManualSign;
        _defaultCurrent     = _defaults.DefaultCurrent;
        // Index 0 is the baseline slot (unused); seed currents for active shots 1..24.
        for (int i = 1; i <= 24; i++) _currents[i] = _defaults.DefaultCurrent;

        await InvokeAsync(StateHasChanged);
    }

    private void StartPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = new System.Threading.Timer(async _ => await PollOnceAsync(), null,
            dueTime: TimeSpan.FromSeconds(2),
            period:  TimeSpan.FromSeconds(2));
    }

    private async Task PollOnceAsync()
    {
        if (_sessionId is null) return;
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<ProcessingSessionStatusDto>(
            $"tools/{Serial}/calibrations/process/{_sessionId}");

        if (!result.IsSuccess) return;
        _status = result.Value;

        // Stop polling once parsing is done — the wizard now drives state
        // forward (compute, save). Failed is also terminal for the parse pass.
        if (_status?.State is nameof(ProcessingSessionState.ReadyForCompute)
                            or nameof(ProcessingSessionState.Computed)
                            or nameof(ProcessingSessionState.Saved)
                            or nameof(ProcessingSessionState.Failed))
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task ComputeAsync()
    {
        if (_sessionId is null) return;
        _computing = true;
        _computeError = null;

        try
        {
            // Active shots are 1..24; index 0 is the baseline and is never
            // an enable target nor included in Currents (Marduk's API takes
            // exactly 24 currents corresponding to active shots).
            var enabled = Enumerable.Range(1, 24).Where(i => _enabled[i]).ToList();
            var activeCurrents = _currents.Skip(1).Take(24).ToList();
            var dto = new ProcessingComputeRequestDto(
                EnabledShotIndices:  enabled,
                GTotal:              _gTotal,
                BTotal:              _bTotal,
                DipDegrees:          _dipDegrees,
                DeclinationDegrees:  _declinationDegrees,
                CoilConstant:        _coilConstant,
                ActiveBDipDegrees:   _activeBDipDegrees,
                SampleRateHz:        _sampleRateHz,
                ManualSign:          _manualSign,
                CurrentsByShot:      activeCurrents);

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostAsync<ProcessingComputeRequestDto, ProcessingResultDto>(
                $"tools/{Serial}/calibrations/process/{_sessionId}/compute", dto);

            if (!result.IsSuccess)
            {
                _computeError = result.Error.AsAlertText();
                return;
            }

            _result = result.Value;
            _calibrationName = $"{Serial}-{DateTime.UtcNow:yyyy-MM-dd}";
            _view = _result?.Success == true ? View.Results : View.Preview;
            if (_result?.Success == false)
                _computeError = _result.Error ?? "Compute returned Success=false.";
        }
        catch (Exception ex)
        {
            _computeError = $"Compute failed: {ex.Message}";
        }
        finally
        {
            _computing = false;
        }
    }

    // ====================== Results / Save ======================

    private async Task SaveWithConfirmAsync()
    {
        // JS confirm via interop: avoids the @onclick + onclick attribute clash
        // and runs server-side after the user accepts in the browser. If they
        // dismiss, no API call is made.
        var confirmed = await JS.InvokeAsync<bool>(
            "confirm",
            "Save this calibration? The previous current calibration for this tool will be flipped to Superseded.");
        if (!confirmed) return;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_sessionId is null || string.IsNullOrWhiteSpace(_calibrationName)) return;
        _saving = true;
        _saveError = null;

        try
        {
            var dto = new ProcessingSaveRequestDto(
                CalibrationName: _calibrationName!,
                CalibratedBy:    string.IsNullOrWhiteSpace(_calibratedBy) ? null : _calibratedBy,
                Notes:           string.IsNullOrWhiteSpace(_notes) ? null : _notes);

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostAsync<ProcessingSaveRequestDto, ProcessingSaveResultDto>(
                $"tools/{Serial}/calibrations/process/{_sessionId}/save", dto);

            if (!result.IsSuccess)
            {
                _saveError = result.Error.AsAlertText();
                return;
            }

            _savedCalibrationId      = result.Value!.CalibrationId;
            _supersededCalibrationId = result.Value.SupersededCalibrationId;
            _view = View.Saved;
        }
        catch (Exception ex)
        {
            _saveError = $"Save failed: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }
    }

    private void GoBackToPreview()
    {
        _view = View.Preview;
        _result = null;
    }
}
