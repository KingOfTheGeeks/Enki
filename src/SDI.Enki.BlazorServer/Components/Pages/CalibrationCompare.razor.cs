using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Calibrations;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tools/{Serial:int}/calibrations/compare")]
[Authorize]
public partial class CalibrationCompare : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public int Serial { get; set; }

    [SupplyParameterFromQuery(Name = "ids")] public string? IdsCsv { get; set; }

    private List<CalibrationDetailDto>? _loaded;
    private List<(CalibrationDetailDto Meta, ToolCalibrationPayload Payload)> _payloads = new();
    private string? _error;

    // Newest by date is the rightmost column; oldest is the baseline against
    // which all the others are diffed.
    private CalibrationDetailDto Baseline => _payloads[0].Meta;
    private ToolCalibrationPayload BaselinePayload => _payloads[0].Payload;

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrWhiteSpace(IdsCsv))
        {
            _error = "No calibration ids supplied. Use the multi-select on the tool's calibration grid.";
            _loaded = new();
            return;
        }

        var ids = IdsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();

        if (ids.Count < 2)
        {
            _error = "Need at least two distinct calibration ids in the URL.";
            _loaded = new();
            return;
        }

        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Fetch in parallel — order by date once we have them all.
        var fetched = await Task.WhenAll(ids.Select(async id =>
            await client.GetAsync<CalibrationDetailDto>($"calibrations/{id}")));

        _loaded = new();
        foreach (var r in fetched)
        {
            if (r.IsSuccess && r.Value is not null)
                _loaded.Add(r.Value);
        }

        _loaded = _loaded.OrderBy(c => c.CalibrationDate).ToList();

        foreach (var meta in _loaded)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ToolCalibrationPayload>(meta.PayloadJson, ReadOptions);
                if (payload is not null)
                    _payloads.Add((meta, payload));
            }
            catch (JsonException ex)
            {
                _error = $"Calibration {meta.Id} payload could not be parsed: {ex.Message}";
            }
        }
    }

    // ---------- delta significance (Nabu's rule) ----------

    private const double AbsThreshold = 0.01;
    private const double PctThreshold = 0.05;
    private const double NearZero     = 1e-10;

    private static bool IsSignificant(double valueA, double valueB)
    {
        var delta = valueB - valueA;
        if (Math.Abs(delta) > AbsThreshold) return true;
        if (Math.Abs(valueA) > NearZero && Math.Abs(delta / valueA) > PctThreshold) return true;
        return false;
    }

    private static string DeltaCss(double valueA, double valueB) =>
        IsSignificant(valueA, valueB) ? "enki-status-pill enki-status-archived" : "enki-mono";

    private static string FormatVal(double v) => v switch
    {
        0.0 => "0.0",
        _ when Math.Abs(v) < 0.0001 => v.ToString("E4"),
        _ => v.ToString("F8"),
    };

    // ---------- Phase B: drift charts (3+ cals) ----------

    private string _trendMagSelection = "accel";

    public sealed record TrendPoint(string Date, double Value);
}
