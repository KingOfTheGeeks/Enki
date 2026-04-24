namespace SDI.Enki.Core.TenantDb.Models;

/// <summary>
/// An immutable snapshot of a <see cref="GradientModel"/> plus its current
/// solver inputs, serialized as JSON. Analysts save these at points of
/// interest so they can replay or compare solutions later.
///
/// <see cref="SaveType"/> is preserved as-is from legacy (integer code);
/// the value-to-meaning mapping stays with the original application until
/// we migrate readers that depend on it.
/// </summary>
public class SavedGradientModel(string name, int gradientModelId, string json)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public DateTimeOffset CreationTime { get; set; } = DateTimeOffset.UtcNow;

    public int GradientModelId { get; set; } = gradientModelId;

    /// <summary>Serialized snapshot payload.</summary>
    public string Json { get; set; } = json;

    /// <summary>Legacy save-type discriminator; integer code, meaning preserved externally.</summary>
    public int SaveType { get; set; }

    public GradientModel? GradientModel { get; set; }
}
