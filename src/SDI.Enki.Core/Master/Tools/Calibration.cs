namespace SDI.Enki.Core.Master.Tools;

/// <summary>
/// A calibration record for a physical <see cref="Tool"/>. Backs Marduk's
/// <c>ICalibrationRegistry</c>. Payload serialises Marduk's <c>ToolCalibration</c>
/// domain model. Fleet-wide — a tool's calibration does not change depending
/// on which tenant it's currently deployed under.
/// </summary>
public class Calibration(Guid toolId, int serialNumber, DateTimeOffset calibrationDate, string payloadJson)
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ToolId { get; set; } = toolId;

    /// <summary>Denormalised tool serial for quick lookup without joining.</summary>
    public int SerialNumber { get; set; } = serialNumber;

    public DateTimeOffset CalibrationDate { get; set; } = calibrationDate;

    /// <summary>Name or username of whoever ran the calibration. Optional.</summary>
    public string? CalibratedBy { get; set; }

    /// <summary>Serialized Marduk <c>ToolCalibration</c> payload.</summary>
    public string PayloadJson { get; set; } = payloadJson;

    // EF nav
    public Tool? Tool { get; set; }
}
