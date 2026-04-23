namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A geological formation intersected by a Well. Resistance values feed into
/// ranging calculations where surrounding rock conductivity matters.
/// </summary>
public class Formation(int wellId, string name, double fromVertical, double toVertical, double resistance)
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public string Name { get; set; } = name;

    public string? Description { get; set; }

    public double FromVertical { get; set; } = fromVertical;
    public double ToVertical { get; set; } = toVertical;

    public double Resistance { get; set; } = resistance;

    // EF nav
    public Well? Well { get; set; }
}
