using Microsoft.Extensions.DependencyInjection;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Migrator.Commands;

internal static class ProvisionCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var parser = new ArgParser(args);

        string code, name;
        try
        {
            code = parser.Require("code");
            name = parser.Require("name");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run 'Enki.Migrator help' for usage.");
            return 1;
        }

        var request = new ProvisionTenantRequest(
            Code:        code,
            Name:        name,
            DisplayName: parser.Get("display"),
            Region:      parser.Get("region"),
            ContactEmail: parser.Get("email"),
            Notes:       parser.Get("notes"));

        await using var scope = services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();

        try
        {
            var result = await svc.ProvisionAsync(request);

            Console.WriteLine();
            Console.WriteLine($"  Tenant provisioned: {result.Code}");
            Console.WriteLine($"  Id:                 {result.TenantId}");
            Console.WriteLine($"  Server:             {result.ServerInstance}");
            Console.WriteLine($"  Active DB:          {result.ActiveDatabaseName}");
            Console.WriteLine($"  Archive DB:         {result.ArchiveDatabaseName} (READ_ONLY)");
            Console.WriteLine($"  Schema version:    {result.AppliedSchemaVersion}");
            Console.WriteLine();
            return 0;
        }
        catch (TenantProvisioningException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"PROVISIONING FAILED: {ex.Message}");
            if (ex.PartialTenantId is { } partial)
                Console.Error.WriteLine($"Partial TenantId={partial} left with Status=Failed for cleanup.");
            if (ex.InnerException is { } inner)
                Console.Error.WriteLine($"Underlying: {inner.GetType().Name}: {inner.Message}");
            return 2;
        }
    }
}
