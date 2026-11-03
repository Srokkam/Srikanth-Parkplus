using IplFranchise.DbMaintenance.Core;
using IplFranchise.DbMaintenance.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IplFranchise.IntegrationTests;

/// <summary>
/// Starts the real API against the local Cosmos DB emulator, in a throw-away database set up exactly like a developer's
/// machine: containers from Schema/schema.json, then every data migration (which adds the opening catalogue: MI Jersey,
/// CSK Cap, RCB Flag, KKR Signed Photo). Nothing is mocked. Tests skip when the emulator isn't running.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;";

    private readonly string connectionString =
        Environment.GetEnvironmentVariable("CosmosDb__ConnectionString") ?? DefaultConnectionString;

    private readonly string databaseName = $"ipl-store-it-{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? factory;
    private ServiceProvider? maintenance;
    private bool emulatorReached;

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var settings = new Dictionary<string, string?>
        {
            ["CosmosDb:ConnectionString"] = connectionString,
            ["CosmosDb:DatabaseName"] = databaseName,
            ["Auth:SigningKey"] = "integration-tests-only-signing-key-0123456789",
            ["ContinuationTokens:SigningKey"] = "integration-tests-only-paging-key-0123456789",
            ["Sweeper:IntervalSeconds"] = "3600",
            ["RateLimiting:SearchPerMinute"] = "100000",
            ["RateLimiting:CheckoutPerMinute"] = "100000",
        };

        maintenance = new ServiceCollection()
            .AddLogging()
            .AddDbMaintenance(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

        try
        {
            await maintenance.GetRequiredService<CosmosClient>().ReadAccountAsync().WaitAsync(TimeSpan.FromSeconds(5));
            emulatorReached = true;
        }
        catch (Exception ex)
        {
            // In CI a skip would hide a broken setup, so there the missing emulator is a failure.
            if (Environment.GetEnvironmentVariable("REQUIRE_COSMOS_EMULATOR") == "true")
            {
                throw new InvalidOperationException("REQUIRE_COSMOS_EMULATOR is set but the Cosmos DB emulator is not reachable.", ex);
            }

            SkipReason = $"Cosmos DB emulator not reachable ({ex.GetType().Name}). Start it with 'docker compose up -d'.";
            return;
        }

        // From here on a failure is a real failure, not a reason to skip.
        await maintenance.GetRequiredService<LocalProvisioner>().ProvisionAsync(CancellationToken.None);
        var migrated = await maintenance.GetRequiredService<MigrationRunner>().MigrateAsync(CancellationToken.None);
        if (migrated.Outcome != MigrateOutcome.Succeeded)
        {
            throw new InvalidOperationException($"Migrating the test database failed: {migrated}.");
        }

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });
    }

    /// <summary>Tests share one database, so each one sets the stock it needs and uses its own customers.</summary>
    public ShopClient Start()
    {
        Assert.SkipUnless(SkipReason is null, SkipReason ?? string.Empty);
        return new ShopClient(factory!);
    }

    public T Service<T>() where T : notnull => factory!.Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        factory?.Dispose();

        if (maintenance is not null)
        {
            // Without an emulator there is no database to delete, and trying would fail the class cleanup.
            if (emulatorReached)
            {
                try
                {
                    await maintenance.GetRequiredService<CosmosClient>().GetDatabase(databaseName).DeleteAsync();
                }
                catch (CosmosException)
                {
                }
            }

            await maintenance.DisposeAsync();
        }
    }
}
