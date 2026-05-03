namespace SDI.Enki.Migrator.Commands;

internal static class HelpCommand
{
    public static int Print()
    {
        Console.WriteLine("""
            Enki Migrator — environment bootstrap, schema fan-out, and tenant provisioning

            Usage:
              Enki.Migrator help
              Enki.Migrator bootstrap-environment
              Enki.Migrator dev-bootstrap
              Enki.Migrator migrate-identity
              Enki.Migrator migrate-master
              Enki.Migrator migrate-tenants  [--all | --tenants CODE1,CODE2] [--only-active|--only-archive] [--parallel N]
              Enki.Migrator migrate          (alias for migrate-tenants — back-compat)
              Enki.Migrator migrate-all      [--tenants ...] [--parallel ...]
              Enki.Migrator provision        --code <CODE> --name <NAME> [--display ...] [--email ...] [--notes ...]
              Enki.Migrator seed-demo-tenants

            Bootstrap (first-time setup of an environment):
              bootstrap-environment runs the full first-deploy sequence:
                1. Identity DB migrations
                2. Master DB migrations + canonical Tools/Calibrations seed
                3. OpenIddict scope + Blazor client + initial admin user
              Idempotent — safe to re-run after rolling deploys (the OIDC
              client and admin user are create-only; existing rows are
              never overwritten by this command).

              Required configuration (no dev fallback in any environment):
                ConnectionStrings:Master      Master DB connection string.
                ConnectionStrings:Identity    Identity DB connection string.
                Identity:Seed:BlazorClientSecret
                                              OIDC client secret; must match the
                                              BlazorServer host's Identity:ClientSecret.
                Identity:Seed:AdminEmail      Initial admin user's email.
                Identity:Seed:AdminPassword   Initial admin user's password — change after first sign-in.

              Required outside Development:
                Identity:Seed:BlazorBaseUri   BlazorServer host's public URL
                                              (e.g. https://dev.sdiamr.com/).

            Migrate (rolling-deploy schema-only updates):
              migrate-identity   apply EF migrations to the Identity DB.
              migrate-master     apply EF migrations to the Master DB + run the
                                 idempotent Tools/Calibrations seed.
              migrate-tenants    apply EF migrations to every tenant DB pair;
                                 filter via --tenants, --only-active, --only-archive.
                                 --parallel N caps in-flight migrations (default 4).
              migrate-all        identity + master + tenants in order; stops on first failure.

            Provision (add a new tenant after the environment is up):
              --code   (required) Tenant code. ^[A-Z][A-Z0-9_]{0,23}$
              --name   (required) Canonical legal name.
              --display      UI-friendly override.
              --email        Primary contact email.
              --notes        Freeform notes.

            Dev convenience:
              dev-bootstrap      One-shot first-boot for the local dev rig:
                                 migrate-identity + migrate-master + full
                                 SeedUsers roster (Mike / Gavin / etc.) +
                                 demo tenants. Refuses to run outside
                                 Development. Used by start-dev.ps1 -Reset.
              seed-demo-tenants  Provision PERMIAN / NORTHSEA / BOREAL with
                                 their canonical demo seed data. Idempotent.

            Configuration sources (precedence highest to lowest):
              command-line                  --key value
              environment variables         ConnectionStrings__Master, etc.
              appsettings.{ENVIRONMENT}.json
              appsettings.json
            """);
        return 0;
    }

    public static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: '{command}'. Run 'Enki.Migrator help' for usage.");
        return 1;
    }
}
