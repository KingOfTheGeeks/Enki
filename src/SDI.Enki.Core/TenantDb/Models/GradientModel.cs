using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Wells;

namespace SDI.Enki.Core.TenantDb.Models;

/// <summary>
/// A modelling scenario for a Gradient ranging computation — pairs a Target
/// Well with an Injection Well plus associated Runs that feed the model.
/// Persisted so analysts can reopen the same scenario across sessions.
/// </summary>
public class GradientModel(string name, int targetWellId, int injectionWellId)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public string? Description { get; set; }

    public int TargetWellId { get; set; } = targetWellId;

    public int InjectionWellId { get; set; } = injectionWellId;

    // EF navs — explicit TargetWell / InjectionWell because Well has two FKs
    // pointing at it. No reverse nav on Well (would double in the UI).
    public Well? TargetWell { get; set; }
    public Well? InjectionWell { get; set; }

    /// <summary>Runs associated with this modeling scenario (Gradient or Passive).</summary>
    public ICollection<Run> Runs { get; set; } = new List<Run>();

    public ICollection<SavedGradientModel> SavedSnapshots { get; set; } = new List<SavedGradientModel>();
}
