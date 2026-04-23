using SDI.Enki.Core.TenantDb.Wells.Enums;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A wellbore — the physical hole in the ground that Surveys describe.
/// In drilling-survey domain: Target (the one being drilled), Injection
/// (casing used as magnetic reference for Gradient runs), or Offset (existing
/// neighboring well referenced for collision avoidance).
/// </summary>
public class Well(string name, WellType type)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public WellType Type { get; set; } = type;

    // EF navs
    public ICollection<Survey> Surveys { get; set; } = new List<Survey>();
    public ICollection<TieOn> TieOns { get; set; } = new List<TieOn>();
    public ICollection<Tubular> Tubulars { get; set; } = new List<Tubular>();
    public ICollection<Formation> Formations { get; set; } = new List<Formation>();
    public ICollection<CommonMeasure> CommonMeasures { get; set; } = new List<CommonMeasure>();
}
