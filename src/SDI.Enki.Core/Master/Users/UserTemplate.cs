namespace SDI.Enki.Core.Master.Users;

/// <summary>
/// Grouping of users for access-pattern attribution. Ported from legacy Athena:
/// "All Team Access", "Technical Team Access", "Senior Team Access".
/// Not currently used for authorization — TenantUser.Role carries that — but
/// preserved for reporting / group-level operations.
/// </summary>
public class UserTemplate(string name, string description)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public string Description { get; set; } = description;

    // EF nav
    public ICollection<User> Users { get; set; } = new List<User>();
}
