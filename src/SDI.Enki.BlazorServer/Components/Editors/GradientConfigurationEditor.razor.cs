using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Editors;

public partial class GradientConfigurationEditor : ComponentBase
{
    private readonly Form _f = new();
    private string _magLocsText = "";

    /// <summary>
    /// Serialize the current form state to the JSON shape
    /// <c>AMR.Core.Gradient.Models.GradientConfiguration</c> expects.
    /// Called by the parent on submit.
    /// </summary>
    public string ToJson()
    {
        _f.MagnetometerLocations = ParseMagLocs(_magLocsText);
        return JsonSerializer.Serialize(_f, JsonOpts);
    }

    /// <summary>
    /// AllowNamedFloatingPointLiterals so NaN / +Inf / -Inf
    /// round-trip as JSON strings ("NaN", "Infinity", "-Infinity").
    /// Several Marduk fields (OperatorProcessing*, RotorInclination/Azimuth)
    /// default to NaN — without this option the serializer throws
    /// at write time. Marduk's deserializer pairs to this setting.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// Parse the magnetometer textarea into a flat double[]. Each
    /// non-empty line is split on commas / whitespace; non-numeric
    /// tokens are dropped silently to keep test entry forgiving.
    /// </summary>
    private static double[] ParseMagLocs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<double>();
        var separators = new[] { ',', ' ', '\t', '\r', '\n' };
        var result = new List<double>();
        foreach (var token in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var d))
                result.Add(d);
        return result.ToArray();
    }

    /// <summary>
    /// Mutable mirror of <c>AMR.Core.Gradient.Models.GradientConfiguration</c>
    /// — the AMR class uses <c>set</c> (not <c>init</c>) so a direct
    /// reference would also work, but keeping a UI-local copy avoids
    /// dragging the AMR project reference into BlazorServer just for
    /// data binding. JSON shape is identical.
    /// </summary>
    private sealed class Form
    {
        public double SensorMeasuredDepthMeter { get; set; }
        public double SensorInclinationDegree { get; set; }
        public double SensorAzimuthDegree { get; set; }
        public double SensorToolfaceDegree { get; set; }
        public double Current { get; set; } = 6.01;
        public bool FlipSign { get; set; }
        public double SampleFrequency { get; set; }
        public double HighCutoffFrequency { get; set; }
        public double LowCutoffFrequency { get; set; }
        public double OperatorProcessingFrequency { get; set; } = double.NaN;
        public double OperatorProcessingBandwidth { get; set; } = double.NaN;
        public double[] MagnetometerLocations { get; set; } = Array.Empty<double>();
    }
}
