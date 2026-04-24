using SDI.Enki.Core.TenantDb.Runs;

namespace SDI.Enki.Core.TenantDb.Operators;

/// <summary>
/// A field hand / rig crew member credited on one or more runs. NOT the
/// oil-company client — in SDI terminology that's the Tenant. These are
/// the humans whose names appear on shot reports.
///
/// Intentionally not linked to <c>AspNetUsers</c> because field hands may
/// not be system users at all.
/// </summary>
public class Operator(string name)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    // EF nav — many Runs can credit this operator
    public ICollection<Run> Runs { get; set; } = new List<Run>();
}
