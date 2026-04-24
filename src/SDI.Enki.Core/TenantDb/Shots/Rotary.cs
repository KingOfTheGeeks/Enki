using SDI.Enki.Core.TenantDb.Runs;

namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// A logical grouping of rotary shots within a rotary Run. Like
/// <see cref="Gradient"/> but for the rotating-dipole pass.
/// <c>RotaryProcessingId</c> and its FK land in a later phase when the
/// RotaryProcessing entity is introduced.
/// </summary>
public class Rotary(string name, int order, Guid runId)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public int Order { get; set; } = order;

    public bool IsValid { get; set; } = true;

    public Guid RunId { get; set; } = runId;

    public int? ParentId { get; set; }

    public DateTime Timestamp { get; set; } = new DateTime(1900, 1, 1);

    public int? Frame { get; set; }

    // EF navs
    public Run? Run { get; set; }
    public Rotary? Parent { get; set; }
    public ICollection<Rotary> Children { get; set; } = new List<Rotary>();
    public ICollection<Shot> Shots { get; set; } = new List<Shot>();
}
