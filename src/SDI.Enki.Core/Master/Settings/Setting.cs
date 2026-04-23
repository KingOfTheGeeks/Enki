using SDI.Enki.Core.Master.Settings.Enums;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Core.Master.Settings;

/// <summary>
/// User-scoped configuration blob. Primary use today is the three default
/// export-profile JSONs (Gradient, Rotary, Passive). Ported from legacy Athena
/// with identical shape.
/// </summary>
public class Setting(string name, SettingType type, string jsonObject, string objectClass)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public SettingType Type { get; set; } = type;

    /// <summary>Serialized profile payload.</summary>
    public string JsonObject { get; set; } = jsonObject;

    /// <summary>Fully-qualified class name of the type <see cref="JsonObject"/> deserializes into.</summary>
    public string ObjectClass { get; set; } = objectClass;

    // EF nav
    public ICollection<User> Users { get; set; } = new List<User>();
}
