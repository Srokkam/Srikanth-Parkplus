using IplFranchise.DbMaintenance.Core;
using IplFranchise.DbMaintenance.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const int Success = 0;
const int Error = 1;
const int SchemaDrift = 2;
const int Busy = 3;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Read appsettings.json from the build output, wherever the tool is started from.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.ClearProviders().AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddDbMaintenance(builder.Configuration);

using var host = builder.Build();
var services = host.Services;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : "help";
var token = cancellation.Token;

try
{
    return command switch
    {
        "provision" => await ProvisionAsync(),
        "verify-schema" => await VerifySchemaAsync(),
        "migrate" => await MigrateAsync(),
        "setup-local" => await SetupLocalAsync(),
        "status" => await StatusAsync(),
        "help" => PrintUsage(Success),
        _ => PrintUsage(Error, $"Unknown command '{command}'."),
    };
}
catch (NonLocalEndpointException ex)
{
    Console.Error.WriteLine(ex.Message);
    return Error;
}
catch (Exception ex)
{
    services.GetRequiredService<ILogger<Program>>().LogError(ex, "'{Command}' failed", command);
    return Error;
}

async Task<int> ProvisionAsync()
{
    await services.GetRequiredService<LocalProvisioner>().ProvisionAsync(token);
    return await VerifySchemaAsync();
}

async Task<int> VerifySchemaAsync()
{
    var drift = await services.GetRequiredService<SchemaVerifier>().VerifyAsync(token);
    return drift.Count == 0 ? Success : SchemaDrift;
}

async Task<int> MigrateAsync()
{
    if (await VerifySchemaAsync() != Success)
    {
        Console.Error.WriteLine("Refusing to run data migrations until the schema matches Schema/schema.json.");
        return SchemaDrift;
    }

    var result = await services.GetRequiredService<MigrationRunner>().MigrateAsync(token);
    return result.Outcome switch
    {
        MigrateOutcome.Succeeded => Success,
        MigrateOutcome.Busy => Busy,
        _ => Error,
    };
}

async Task<int> SetupLocalAsync()
{
    var created = await services.GetRequiredService<LocalProvisioner>().ProvisionAsync(token);
    var exitCode = await MigrateAsync();

    Console.WriteLine($"setup-local: created {created} database/container(s); see above for migrations.");
    return exitCode;
}

async Task<int> StatusAsync()
{
    var statuses = await services.GetRequiredService<MigrationRunner>().GetStatusAsync(token);
    Console.WriteLine($"{"Migration",-36}{"Status",-11}{"Started (UTC)",-22}{"Completed (UTC)",-22}Error");

    foreach (var (migration, record) in statuses)
    {
        Console.WriteLine(
            $"{migration.Id,-36}{record?.Status ?? "pending",-11}{record?.StartedUtc.ToString("u") ?? "",-22}" +
            $"{record?.CompletedUtc?.ToString("u") ?? "",-22}{record?.Error}");
    }

    return Success;
}

static int PrintUsage(int exitCode, string? error = null)
{
    if (error is not null)
    {
        Console.Error.WriteLine(error);
    }

    Console.WriteLine("""
        Cosmos DB maintenance for the IPL store (database and containers declared in Schema/schema.json)

        Usage:
          dotnet run --project IplFranchise.DbMaintenance -- <command> [--CosmosDb:Setting=value]

        Commands:
          provision       Local emulator only. Create the database and any missing containers, then verify.
                          Never changes an existing container.
          verify-schema   Compare every container with Schema/schema.json (partition key, TTL, indexing).
          migrate         verify-schema, then apply pending data migrations in id order.
          setup-local     provision + migrate. A second run creates and changes nothing.
          status          Show each migration's record in the 'migrations' container.
          help            Show this message.

        Exit codes: 0 success, 1 error, 2 schema drift, 3 another runner is applying a migration.

        Settings come from appsettings.json, then environment variables (CosmosDb__ConnectionString),
        then the command line (--CosmosDb:ConnectionString="..."). A connection string without AccountKey
        signs in with Entra ID (DefaultAzureCredential).
        """);

    return exitCode;
}
