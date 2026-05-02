namespace SDI.Enki.Core.Master.Users;

/// <summary>
/// Grouping of users for access-pattern attribution. Ported from legacy Athena:
/// "All Team Access", "Technical Team Access", "Senior Team Access".
/// Not used for authorization — TeamSubtype on AspNetUsers (Field /
/// Office / Supervisor) carries the system-wide gate, and capability
/// claims handle additive grants. Preserved here for reporting and
/// group-level operations only.
/// </summary>
public class UserTemplate(string name, string description)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public string Description { get; set; } = description;

    // EF nav
    public ICollection<User> Users { get; set; } = new List<User>();
}
