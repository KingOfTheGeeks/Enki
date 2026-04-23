namespace SDI.Enki.Migrator.Commands;

internal static class HelpCommand
{
    public static int Print()
    {
        Console.WriteLine("""
            Enki Migrator — tenant provisioning + schema fan-out

            Usage:
              Enki.Migrator provision --code <CODE> --name <NAME> [options]
              Enki.Migrator migrate   [--all | --tenants CODE1,CODE2] [options]
              Enki.Migrator help

            Provision options:
              --code        (required) Tenant code. ^[A-Z][A-Z0-9_]{0,23}$
              --name        (required) Canonical legal name.
              --display     UI-friendly override for the name.
              --region      Optional region tag (e.g. Permian, GoM).
              --email       Primary contact email.
              --notes       Freeform notes.

            Migrate options:
              --all            Apply migrations to every tenant DB (default).
              --tenants X,Y,Z  Restrict to a comma-separated list of tenant codes.
              --only-active    Skip Archive DBs.
              --only-archive   Skip Active DBs.
              --parallel N     Max concurrent migrations (default 4).

            Connection strings come from ConnectionStrings:Master in
            appsettings (or the ConnectionStrings__Master env var).
            """);
        return 0;
    }

    public static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: '{command}'. Run 'Enki.Migrator help' for usage.");
        return 1;
    }
}
