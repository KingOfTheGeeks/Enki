using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Wells;

namespace SDI.Enki.Core.TenantDb.Models;

/// <summary>
/// A modelling scenario for a Rotary ranging computation — target + intercept
/// wells paired with the Rotary runs that feed it.
/// </summary>
public class RotaryModel(string name, int targetWellId, int interceptWellId)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public string Description { get; set; } = string.Empty;

    public int TargetWellId { get; set; } = targetWellId;
    public int InterceptWellId { get; set; } = interceptWellId;

    public Well? TargetWell { get; set; }
    public Well? InterceptWell { get; set; }

    public ICollection<Run> Runs { get; set; } = new List<Run>();
}
