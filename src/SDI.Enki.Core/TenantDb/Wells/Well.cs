using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells.Enums;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A wellbore — the physical hole in the ground that Surveys describe.
/// In drilling-survey domain: Target (the one being drilled), Injection
/// (casing used as magnetic reference for Gradient runs), or Offset (existing
/// neighboring well referenced for collision avoidance).
///
/// Implements <see cref="IAuditable"/> — CreatedAt / CreatedBy / UpdatedAt /
/// UpdatedBy / RowVersion are managed by
/// <c>TenantDbContext.SaveChangesAsync</c>; don't set them from business code.
/// </summary>
public class Well(string name, WellType type) : IAuditable
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public WellType Type { get; set; } = type;

    // IAuditable — managed by TenantDbContext.SaveChangesAsync; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF navs
    public ICollection<Survey> Surveys { get; set; } = new List<Survey>();
    public ICollection<TieOn> TieOns { get; set; } = new List<TieOn>();
    public ICollection<Tubular> Tubulars { get; set; } = new List<Tubular>();
    public ICollection<Formation> Formations { get; set; } = new List<Formation>();
    public ICollection<CommonMeasure> CommonMeasures { get; set; } = new List<CommonMeasure>();
}
