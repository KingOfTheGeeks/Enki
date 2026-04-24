namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Binary payload attached to a <see cref="Passive"/>. CASCADE-deleted
/// with the parent Passive.
/// </summary>
public class PassiveFile(int passiveId, string name)
{
    public int Id { get; set; }

    public int PassiveId { get; set; } = passiveId;

    public string Name { get; set; } = name;

    public byte[]? File { get; set; }

    public DateTime Timestamp { get; set; } = new DateTime(1900, 1, 1);

    // EF nav
    public Passive? Passive { get; set; }
}
