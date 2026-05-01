using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Shared.Seeding;

namespace SDI.Enki.Infrastructure.Data;

/// <summary>
/// Applies baseline seed data to the master database — the SDI team
/// users and 3 role templates. User identities (both
/// <c>SDI.Enki.Core.Master.Users.User.Id</c> and the AspNetUsers
/// <c>IdentityId</c> bridge) come from the canonical roster in
/// <see cref="SeedUsers"/> so this seed and the Identity host's
/// <c>IdentitySeedData</c> can't drift on the GUIDs that pin them
/// together.
/// </summary>
internal static class MasterSeedData
{
    private const int AllTemplateId       = 1;
    private const int TechnicalTemplateId = 2;
    private const int SeniorTemplateId    = 3;

    public static void Apply(ModelBuilder b)
    {
        SeedUserTemplates(b);
        SeedUsersInto(b);
        SeedUserTemplateAssignments(b);
    }

    private static void SeedUserTemplates(ModelBuilder b)
    {
        b.Entity<UserTemplate>().HasData(
            new { Id = AllTemplateId,       Name = "All Team Access",       Description = "Default security template; contains access for all team members." },
            new { Id = TechnicalTemplateId, Name = "Technical Team Access", Description = "Technical security template; contains access for technical team members." },
            new { Id = SeniorTemplateId,    Name = "Senior Team Access",    Description = "Senior security template; contains access for senior team members." }
        );
    }

    private static void SeedUsersInto(ModelBuilder b)
    {
        // Tenant-type users do NOT get a master User row — they're
        // hard-bound via ApplicationUser.TenantId on the Identity side
        // and never appear in TenantUser membership tables. Their
        // MasterUserId on SeedUser is a placeholder the master seeder
        // ignores. Filter to Team users only so the master.User table
        // stays "SDI staff" — what every join downstream assumes.
        var rows = SeedUsers.All
            .Where(u => u.UserType == "Team")
            .Select(u => (object)new
            {
                Id         = u.MasterUserId,
                Name       = u.Username,
                IdentityId = u.IdentityId,
            })
            .ToArray();

        b.Entity<User>().HasData(rows);
    }

    private static void SeedUserTemplateAssignments(ModelBuilder b)
    {
        // Mirrors the legacy Athena user↔template mapping. Named statics
        // on SeedUsers make the assignments readable; the GUIDs come from
        // the same source the User HasData uses above.
        var assignments = new (int TemplateId, Guid UserId)[]
        {
            // All Team Access — everyone
            (AllTemplateId, SeedUsers.DapoAjayi.MasterUserId),
            (AllTemplateId, SeedUsers.JamieDorey.MasterUserId),
            (AllTemplateId, SeedUsers.AdamKarabasz.MasterUserId),
            (AllTemplateId, SeedUsers.DouglasRidgway.MasterUserId),
            (AllTemplateId, SeedUsers.TravisSolomon.MasterUserId),
            (AllTemplateId, SeedUsers.MikeKing.MasterUserId),
            (AllTemplateId, SeedUsers.JamesPowell.MasterUserId),
            (AllTemplateId, SeedUsers.JoelHarrison.MasterUserId),
            (AllTemplateId, SeedUsers.ScottBrandel.MasterUserId),
            (AllTemplateId, SeedUsers.JohnBorders.MasterUserId),
            (AllTemplateId, SeedUsers.GavinHelboe.MasterUserId),

            // Technical Team — engineering leads
            (TechnicalTemplateId, SeedUsers.MikeKing.MasterUserId),
            (TechnicalTemplateId, SeedUsers.DouglasRidgway.MasterUserId),
            (TechnicalTemplateId, SeedUsers.GavinHelboe.MasterUserId),

            // Senior Team — leadership
            (SeniorTemplateId, SeedUsers.JamieDorey.MasterUserId),
            (SeniorTemplateId, SeedUsers.JoelHarrison.MasterUserId),
        };

        var rows = assignments
            .Select(a => (object)new { UsersId = a.UserId, TemplatesId = a.TemplateId })
            .ToArray();

        b.Entity<User>()
         .HasMany(u => u.Templates)
         .WithMany(t => t.Users)
         .UsingEntity(j => j.ToTable("UserUserTemplate").HasData(rows));
    }
}
