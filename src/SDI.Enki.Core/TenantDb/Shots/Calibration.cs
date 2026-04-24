namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Named calibration payload attached to a Shot. Distinct from
/// <c>SDI.Enki.Core.Master.Tools.Calibration</c> (which lives in the master
/// DB and carries the rich ToolCalibration payload) — this is the per-tenant
/// lookup that matches the legacy Athena <c>Calibrations</c> shape.
///
/// UNIQUE INDEX on (Name, CalibrationString). Writers must go through
/// <c>IEntityLookup.FindOrCreateAsync</c>.
/// </summary>
public class Calibration(string name, string calibrationString)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;
    public string CalibrationString { get; set; } = calibrationString;
}
