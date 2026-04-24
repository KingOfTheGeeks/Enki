namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Binary payload attached to a <see cref="Rotary"/>. CASCADE-deleted
/// with the parent Rotary.
/// </summary>
public class RotaryFile(int rotaryId, string name)
{
    public int Id { get; set; }

    public int RotaryId { get; set; } = rotaryId;

    public string Name { get; set; } = name;

    public byte[]? File { get; set; }

    public DateTime Timestamp { get; set; } = new DateTime(1900, 1, 1);

    // EF nav
    public Rotary? Rotary { get; set; }
}
