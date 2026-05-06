using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Editors;

public partial class RotatingDipoleConfigurationEditor : ComponentBase
{
    private readonly Form _f = new();

    public string ToJson() => JsonSerializer.Serialize(_f, JsonOpts);

    /// <summary>AllowNamedFloatingPointLiterals — RotorInclination/Azimuth
    /// + OperatorProcessing* default to NaN; STJ throws without this.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>UI-local mirror of RotatingDipoleConfiguration with <c>set</c> accessors.</summary>
    private sealed class Form
    {
        public double SensorMeasuredDepthMeter { get; set; }
        public double SensorInclinationDegree { get; set; }
        public double SensorAzimuthDegree { get; set; }
        public double SensorToolfaceDegree { get; set; }
        public double SensorShieldMatrixXy { get; set; }
        public double SensorShieldMatrixZ { get; set; }
        public int SensorMagnetometerIndex { get; set; }
        public bool IsFrontLobe { get; set; }
        public double RotorMoment { get; set; }
        public double RotorInclinationDegree { get; set; } = double.NaN;
        public double RotorAzimuthDegree { get; set; } = double.NaN;
        public double SampleFrequency { get; set; }
        public double HighCutoffFrequency { get; set; }
        public double LowCutoffFrequency { get; set; }
        public double OperatorProcessingFrequency { get; set; } = double.NaN;
        public double OperatorProcessingBandwidth { get; set; } = double.NaN;
        public double Sigma { get; set; }
        public double Tolerance { get; set; }
    }
}
