namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Raw tool-orientation sample attached to a <see cref="Shot"/>. Unified from
/// legacy <c>ToolSurveys</c> + <c>RotaryToolSurveys</c>; <see cref="Current"/>
/// is Gradient-only and nullable for Rotary-sourced rows.
/// </summary>
public class ToolSurvey
{
    public int Id { get; set; }

    public int ShotId { get; set; }

    public double Depth { get; set; }
    public double Inclination { get; set; }
    public double Azimuth { get; set; }
    public double GravityToolface { get; set; }
    public double MagneticToolface { get; set; }
    public double Temperature { get; set; }

    /// <summary>Gradient-only.</summary>
    public double? Current { get; set; }

    public double Gx { get; set; }
    public double Gy { get; set; }
    public double Gz { get; set; }
    public double GTotal { get; set; }

    public double Bx { get; set; }
    public double By { get; set; }
    public double Bz { get; set; }
    public double BTotal { get; set; }
    public double Dip { get; set; }

    public double Mag1Ab { get; set; }
    public double Mag2Ab { get; set; }
    public double Mag3Ab { get; set; }
    public double Mag4Ab { get; set; }

    // EF nav
    public Shot? Shot { get; set; }
}
