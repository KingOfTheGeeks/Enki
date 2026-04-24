namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Per-magnetometer active-field decomposition attached to a <see cref="Shot"/>.
/// Unified from legacy <c>ActiveFields</c> + <c>RotaryActiveFields</c>; the
/// two legacy tables had identical shape, so no columns become nullable in
/// the merge — this is the cleanest of the unifications.
/// </summary>
public class ActiveField
{
    public int Id { get; set; }

    public int ShotId { get; set; }

    public int Mag { get; set; }
    public double Field { get; set; }

    public double CosX { get; set; }
    public double CosY { get; set; }
    public double CosZ { get; set; }

    public double SinX { get; set; }
    public double SinY { get; set; }
    public double SinZ { get; set; }

    // EF nav
    public Shot? Shot { get; set; }
}
