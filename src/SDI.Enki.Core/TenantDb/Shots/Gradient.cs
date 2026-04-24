using SDI.Enki.Core.TenantDb.Comments;
using SDI.Enki.Core.TenantDb.Runs;

namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// A logical grouping of gradient shots within a gradient Run. Groups of
/// Gradients can form trees via <see cref="ParentId"/> — typically "primary"
/// and "retake" clusters. Parent of many <see cref="Shot"/>s whose
/// <c>GradientId</c> references this.
/// </summary>
public class Gradient(string name, int order, Guid runId)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    /// <summary>Display order within the parent Run.</summary>
    public int Order { get; set; } = order;

    public bool IsValid { get; set; } = true;

    public Guid RunId { get; set; } = runId;

    /// <summary>Optional parent Gradient for hierarchical grouping.</summary>
    public int? ParentId { get; set; }

    public DateTime Timestamp { get; set; } = new DateTime(1900, 1, 1);

    public double? Voltage { get; set; }
    public double? Frequency { get; set; }
    public int? Frame { get; set; }

    // EF navs
    public Run? Run { get; set; }
    public Gradient? Parent { get; set; }
    public ICollection<Gradient> Children { get; set; } = new List<Gradient>();
    public ICollection<Shot> Shots { get; set; } = new List<Shot>();
    public ICollection<GradientSolution> Solutions { get; set; } = new List<GradientSolution>();
    public ICollection<GradientFile> Files { get; set; } = new List<GradientFile>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
}
