namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Magnetic reference values (geomagnetic field) for a Shot location — BTotal
/// magnitude, Dip angle, and Declination. Stored as a lookup; identical triples
/// are shared across many shots rather than duplicated inline.
///
/// Uniqueness is enforced by a DB-level UNIQUE INDEX on (BTotal, Dip,
/// Declination). Writers must go through <c>IEntityLookup.FindOrCreateAsync</c>
/// to avoid duplicate-key exceptions — this is the repository-layer
/// replacement for the legacy AFTER-INSERT dedup trigger.
/// </summary>
public class Magnetics(double bTotal, double dip, double declination)
{
    public int Id { get; set; }

    public double BTotal { get; set; } = bTotal;
    public double Dip { get; set; } = dip;
    public double Declination { get; set; } = declination;
}
