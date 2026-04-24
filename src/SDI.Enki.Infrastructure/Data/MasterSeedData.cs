using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Infrastructure.Data;

/// <summary>
/// Applies baseline seed data to the master database — the 12 SDI team users
/// and 3 role templates, ported verbatim from legacy Athena's seed. Using
/// the same GUIDs preserves continuity with existing identity tokens and
/// makes cross-referencing legacy records trivial.
/// </summary>
internal static class MasterSeedData
{
    // IdentityIds (from legacy Identity DB's AspNetUsers.Id)
    private static readonly Guid DapoIdentityId    = Guid.Parse("8cf4b730-c619-49d0-8ed7-be0ac89de718");
    private static readonly Guid JamieIdentityId   = Guid.Parse("f8aff5b3-473b-436f-9592-186cb28ac848");
    private static readonly Guid AdamIdentityId    = Guid.Parse("dafd065f-4790-4235-9db0-6f47abadf3aa");
    private static readonly Guid DouglasIdentityId = Guid.Parse("bd34385d-2d88-4781-bef5-e955ddaa8293");
    private static readonly Guid TravisIdentityId  = Guid.Parse("e5a7f984-688a-4904-8155-3fe724584385");
    private static readonly Guid MikeIdentityId    = Guid.Parse("1e333b45-1448-4b26-a68d-b4effbbdcd9d");
    private static readonly Guid JamesIdentityId   = Guid.Parse("a72f07d8-9a12-4825-95f4-7c5bbea6e6e5");
    private static readonly Guid JoelIdentityId    = Guid.Parse("f8d3ceda-ce98-4825-88f9-c8e8356a61db");
    private static readonly Guid ScottIdentityId   = Guid.Parse("bc120086-fc2d-4f41-b76a-3f6c3536c2cc");
    private static readonly Guid JohnIdentityId    = Guid.Parse("d92be0d5-dfbe-4d1d-9823-1ca37617dade");
    private static readonly Guid GavinIdentityId   = Guid.Parse("2c4f110e-adc4-4759-aa34-b73ec0954c9e");

    // User Ids (same GUIDs as legacy Athena.User for continuity)
    private static readonly Guid DapoId    = Guid.Parse("7a519cae-da73-41df-82dd-05fbc8bc73a0");
    private static readonly Guid JamieId   = Guid.Parse("e8dd0c2a-bceb-4885-a90e-8f9cf446ee5a");
    private static readonly Guid AdamId    = Guid.Parse("ab3f526a-849b-492b-91d9-f3851e978869");
    private static readonly Guid DouglasId = Guid.Parse("02c9751a-3058-4e15-b5c5-ce82adaebaeb");
    private static readonly Guid TravisId  = Guid.Parse("123505bf-cd91-4e15-b583-ad1291347508");
    private static readonly Guid MikeId    = Guid.Parse("f5fd1207-1dc6-49c7-a794-b5420bd88008");
    private static readonly Guid JamesId   = Guid.Parse("ce17bb43-1eac-439e-80a5-324a3edaf373");
    private static readonly Guid JoelId    = Guid.Parse("e48bacc4-4375-4445-88b0-e08c20216513");
    private static readonly Guid ScottId   = Guid.Parse("050add37-54b3-4996-9bcc-8ed3cc4992b6");
    private static readonly Guid JohnId    = Guid.Parse("0c2c609c-abb0-4009-8928-e274352caf11");
    private static readonly Guid GavinId   = Guid.Parse("466ba5fd-d339-4a92-93bc-ec3354a98945");

    // UserTemplate Ids
    private const int AllTemplateId       = 1;
    private const int TechnicalTemplateId = 2;
    private const int SeniorTemplateId    = 3;

    public static void Apply(ModelBuilder b)
    {
        SeedUserTemplates(b);
        SeedUsers(b);
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

    private static void SeedUsers(ModelBuilder b)
    {
        b.Entity<User>().HasData(
            new { Id = DapoId,    Name = "dapo.ajayi",      IdentityId = DapoIdentityId },
            new { Id = JamieId,   Name = "jamie.dorey",     IdentityId = JamieIdentityId },
            new { Id = AdamId,    Name = "adam.karabasz",   IdentityId = AdamIdentityId },
            new { Id = DouglasId, Name = "douglas.ridgway", IdentityId = DouglasIdentityId },
            new { Id = TravisId,  Name = "travis.solomon",  IdentityId = TravisIdentityId },
            new { Id = MikeId,    Name = "mike.king",       IdentityId = MikeIdentityId },
            new { Id = JamesId,   Name = "james.powell",    IdentityId = JamesIdentityId },
            new { Id = JoelId,    Name = "joel.harrison",   IdentityId = JoelIdentityId },
            new { Id = ScottId,   Name = "scott.brandel",   IdentityId = ScottIdentityId },
            new { Id = JohnId,    Name = "john.borders",    IdentityId = JohnIdentityId },
            new { Id = GavinId,   Name = "gavin.helboe",    IdentityId = GavinIdentityId }
        );
    }

    private static void SeedUserTemplateAssignments(ModelBuilder b)
    {
        // Mirrors the legacy Athena user↔template mapping exactly.
        var assignments = new[]
        {
            // All Team Access — everyone
            (AllTemplateId, DapoId), (AllTemplateId, JamieId), (AllTemplateId, AdamId),
            (AllTemplateId, DouglasId), (AllTemplateId, TravisId), (AllTemplateId, MikeId),
            (AllTemplateId, JamesId), (AllTemplateId, JoelId), (AllTemplateId, ScottId),
            (AllTemplateId, JohnId), (AllTemplateId, GavinId),

            // Technical Team — engineering leads
            (TechnicalTemplateId, MikeId), (TechnicalTemplateId, DouglasId),
            (TechnicalTemplateId, GavinId),

            // Senior Team — leadership
            (SeniorTemplateId, JamieId), (SeniorTemplateId, JoelId),
        };

        var rows = assignments
            .Select(a => new { UsersId = a.Item2, TemplatesId = a.Item1 })
            .Cast<object>()
            .ToArray();

        b.Entity<User>()
         .HasMany(u => u.Templates)
         .WithMany(t => t.Users)
         .UsingEntity(j => j.ToTable("UserUserTemplate").HasData(rows));
    }
}
