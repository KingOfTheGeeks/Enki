using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Surveys;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

public partial class SurveyImportButton : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Parameter, EditorRequired] public string TenantCode { get; set; } = "";
    [Parameter, EditorRequired] public Guid   JobId      { get; set; }
    [Parameter, EditorRequired] public int    WellId     { get; set; }

    /// <summary>
    /// Job's preferred display unit-system. Used by the conflict
    /// prompt's existing-vs-imported tie-on summary so depths /
    /// Northing / Easting render in the same units the rest of the
    /// page does. Defaults to strict SI; the parent should pass the
    /// resolved <c>_units</c> through. Angles are pass-through
    /// degrees regardless of preset.
    /// </summary>
    [Parameter] public UnitSystem Units { get; set; } = UnitSystem.SI;

    /// <summary>
    /// Parent-supplied callback that re-fetches the surveys + tie-on
    /// payload in place. Invoked on successful import (and on the
    /// retry-with-decision path) so the grid picks up the freshly
    /// imported rows without a full page reload — the old
    /// <c>Nav.NavigateTo(forceLoad: true)</c> shape was the source of
    /// the visible page-tear flash on every import.
    /// </summary>
    [Parameter] public EventCallback OnReload { get; set; }

    /// <summary>20 MB matches the WebApi RequestSizeLimit on /import.</summary>
    private const long MaxFileBytes = 20_000_000;

    private bool _uploading;
    private string? _message;
    private string _alertClass = "alert-info";
    private IReadOnlyList<SurveyImportNoteDto>? _notes;

    /// <summary>Structured server error — rendered via &lt;ApiErrorAlert&gt;
    /// so per-field reasons (the importer's [CODE] notes, the "file is
    /// required" model-binding error, etc.) reach the user instead of
    /// being collapsed into the bare title.</summary>
    private ApiError? _error;

    /// <summary>Buffered file body — kept around so a conflict-retry can re-POST without re-prompting the file dialog.</summary>
    private byte[]? _pendingBytes;
    private string? _pendingFileName;
    private string? _pendingContentType;

    /// <summary>Conflict info parsed out of the server's 409 ProblemDetails extensions.</summary>
    private ConflictDetails? _pendingConflict;

    private async Task HandleFileSelected(InputFileChangeEventArgs args)
    {
        // Buffer once so we can re-POST on conflict-retry without
        // asking the user to pick the file again.
        var file = args.File;
        await using (var stream = file.OpenReadStream(maxAllowedSize: MaxFileBytes))
        await using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms);
            _pendingBytes = ms.ToArray();
        }
        _pendingFileName    = file.Name;
        _pendingContentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        await PostBufferedAsync(keepExistingTieOn: null);
    }

    private async Task RetryWithDecisionAsync(bool keepExisting)
    {
        // User chose from the conflict prompt — clear the prompt and
        // re-POST with the explicit flag.
        _pendingConflict = null;
        await PostBufferedAsync(keepExistingTieOn: keepExisting);
    }

    private void CancelPending()
    {
        _pendingConflict    = null;
        _pendingBytes       = null;
        _pendingFileName    = null;
        _pendingContentType = null;
        _message            = "Import cancelled — no changes saved.";
        _alertClass         = "alert-info";
    }

    private async Task PostBufferedAsync(bool? keepExistingTieOn)
    {
        if (_pendingBytes is null || _pendingFileName is null)
        {
            _message = "No file buffered.";
            _alertClass = "alert-danger";
            return;
        }

        _uploading       = true;
        _message         = null;
        _error           = null;
        _notes           = null;
        _alertClass      = "alert-info";
        StateHasChanged();

        using var content = new MultipartFormDataContent();
        var bodyContent = new ByteArrayContent(_pendingBytes);
        bodyContent.Headers.ContentType = new MediaTypeHeaderValue(_pendingContentType!);
        content.Add(bodyContent, "file", _pendingFileName);

        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var url    = $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/surveys/import" +
                         (keepExistingTieOn is bool keep
                             ? $"?keepExistingTieOn={(keep ? "true" : "false")}"
                             : "");
            var result = await client.PostMultipartAsync<SurveyImportResultDto>(url, content);

            if (result.IsSuccess)
            {
                await HandleSuccess(result.Value);
                return;
            }

            // 409 surfaces with structured `existingTieOn` / `importedTieOn`
            // members on ProblemDetails.Extensions. The server's contract
            // is documented on SurveysController.Import; the importer ships
            // them as nested objects (JsonElement after deserialisation).
            if (result.Error.StatusCode == StatusCodes.Status409Conflict
                && result.Error.Extensions is { } extensions)
            {
                _pendingConflict = ParseConflict(extensions, Units);
                _message = null;            // hide any previous error; the prompt is the new state
                _error = null;
                return;
            }

            _error = result.Error;
            ClearPendingBuffer();
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task HandleSuccess(SurveyImportResultDto result)
    {
        var unitNote = result.DepthUnitWasDetected
            ? $"(detected {result.DetectedDepthUnit.ToLower()})"
            : $"(assumed {result.DetectedDepthUnit.ToLower()})";
        var tieOnSuffix = result.TieOnsCreated > 0 ? $" + {result.TieOnsCreated} tie-on" : "";

        _message = $"Imported {result.SurveysImported} survey {(result.SurveysImported == 1 ? "station" : "stations")}{tieOnSuffix} from {result.DetectedFormat} {unitNote}.";
        _notes = result.Notes;
        _alertClass = "alert-success";
        ClearPendingBuffer();

        // Ask the parent to re-fetch in place (LoadAsync on Surveys.razor)
        // so the grid renders the freshly-calculated rows without the
        // full page reload that the old Nav.NavigateTo(forceLoad: true)
        // triggered. The parent's StateHasChanged on completion brings
        // the new data through.
        await OnReload.InvokeAsync();
    }

    /// <summary>
    /// Parses the 409 ProblemDetails extensions (<c>existingTieOn</c> /
    /// <c>importedTieOn</c>) into a small summary the user can read at
    /// a glance. Falls back to a "couldn't parse" message if the shape
    /// doesn't match — keeps the prompt populated against a future
    /// server-side change. Length-bearing fields (depth, northing,
    /// easting, vertical reference) project through <paramref name="units"/>
    /// so the prompt agrees with what the surrounding grid is showing —
    /// a Field tenant sees ft, a Metric tenant sees m. Angles stay in
    /// degrees regardless.
    /// </summary>
    private static ConflictDetails ParseConflict(
        IReadOnlyDictionary<string, object?> extensions,
        UnitSystem units)
    {
        return new ConflictDetails(
            ExistingSummary: SummariseTieOn(extensions, "existingTieOn", units),
            ImportedSummary: SummariseTieOn(extensions, "importedTieOn", units));
    }

    private static string SummariseTieOn(
        IReadOnlyDictionary<string, object?> extensions,
        string propertyName,
        UnitSystem units)
    {
        if (!extensions.TryGetValue(propertyName, out var raw) || raw is not JsonElement t)
            return "(not provided)";

        double D(string n) => t.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        // Length values arrive in canonical SI (m); project to the
        // tenant's preset before rendering. Use Measurement.Format so
        // the abbreviation comes from the same source as the rest of
        // the page — no risk of "ft" / "(ft)" drift between cells and
        // the conflict prompt.
        string Length(string n) => Measurement.FromSi(D(n), EnkiQuantity.Length).Format(units, "F2");

        return $"depth {Length("depth")}, inc {D("inclination"):F2}°, az {D("azimuth"):F2}°, " +
               $"N {Length("northing")}, E {Length("easting")}, " +
               $"VR {Length("verticalReference")}";
    }

    private void ClearPendingBuffer()
    {
        _pendingBytes       = null;
        _pendingFileName    = null;
        _pendingContentType = null;
    }

    /// <summary>Plain-text rendering of the existing vs imported tie-ons for the conflict prompt.</summary>
    private sealed record ConflictDetails(string ExistingSummary, string ImportedSummary);
}
