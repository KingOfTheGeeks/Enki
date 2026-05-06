using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Editors;

public partial class RotatingDipoleSolutionEditor : ComponentBase
{
    private readonly Form _f = new();
    private string _messagesText = "";

    public string ToJson()
    {
        _f.Messages = ParseLines(_messagesText);
        return JsonSerializer.Serialize(_f, JsonOpts);
    }

    /// <summary>AllowNamedFloatingPointLiterals — see editor sibling docs.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static List<string> ParseLines(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.TrimEnd('\r').Trim())
                  .Where(s => s.Length > 0)
                  .ToList();

    /// <summary>UI-local mirror of RotatingDipoleSolution with <c>set</c> accessors.</summary>
    private sealed class Form
    {
        public double SensorMeasuredDepthMeter { get; set; }
        public double SensorInclinationDegree { get; set; }
        public double SensorAzimuthDegree { get; set; }
        public double SensorGravityToolfaceDegree { get; set; }
        public double RotorInclinationDegree { get; set; }
        public double RotorAzimuthDegree { get; set; }
        public double RotorFrequencyHz { get; set; }
        public double RotorBandwidthHz { get; set; }
        public double PassByTotalDistanceMeter { get; set; }
        public double PassByApproachAngleDegree { get; set; }
        public double DistanceToPassByMeter { get; set; }
        public double DistanceAtPassByMeter { get; set; }
        public double NorthToSensorMeter { get; set; }
        public double EastToSensorMeter { get; set; }
        public double VerticalToSensorMeter { get; set; }
        public double HighSideToSensorMeter { get; set; }
        public double RightSideToSensorMeter { get; set; }
        public double AxialToSensorMeter { get; set; }
        public List<string> Messages { get; set; } = new();
    }
}
