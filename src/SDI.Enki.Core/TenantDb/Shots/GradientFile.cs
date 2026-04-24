namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Binary payload attached to a <see cref="Gradient"/> — typically raw tool
/// telemetry, sensor dumps, or processing artifacts. CASCADE-deleted with
/// the parent Gradient.
/// </summary>
public class GradientFile(int gradientId, string name)
{
    public int Id { get; set; }

    public int GradientId { get; set; } = gradientId;

    public string Name { get; set; } = name;

    /// <summary>Raw bytes. Nullable in case the row is a placeholder.</summary>
    public byte[]? File { get; set; }

    public DateTime Timestamp { get; set; } = new DateTime(1900, 1, 1);

    // EF nav
    public Gradient? Gradient { get; set; }
}
