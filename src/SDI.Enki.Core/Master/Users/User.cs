using SDI.Enki.Core.Master.Tenants;

namespace SDI.Enki.Core.Master.Users;

/// <summary>
/// SDI team member. Mirrors the legacy Athena User entity. <see cref="IdentityId"/>
/// points at AspNetUsers.Id in the separate Identity database.
/// </summary>
public class User(string name, Guid identityId)
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = name;

    public Guid IdentityId { get; set; } = identityId;

    // EF navs
    public ICollection<UserTemplate> Templates { get; set; } = new List<UserTemplate>();
    public ICollection<TenantUser> Tenants { get; set; } = new List<TenantUser>();
}
