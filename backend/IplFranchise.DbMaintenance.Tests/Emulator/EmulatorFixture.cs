using System.Text.Json.Nodes;
using IplFranchise.DbMaintenance.Configuration;
using IplFranchise.DbMaintenance.Core;
using IplFranchise.DbMaintenance.Schema;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IplFranchise.DbMaintenance.Tests.Emulator;

/// <summary>
/// Connects to the local emulator (CosmosDb__ConnectionString, default http://localhost:8081).
/// Tests skip when it isn't reachable. Each test gets its own throw-away database.
/// </summary>
public sealed class EmulatorFixture : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("CosmosDb__ConnectionString") ?? DefaultConnectionString;

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!CosmosClientFactory.IsLocalEndpoint(ConnectionString))
        {
            SkipReason = "Emulator tests only run against a local endpoint.";
            return;
        }

        try
        {
            using var client = CosmosClientFactory.Create(ConnectionString);
            await client.ReadAccountAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            // In CI a skip would hide a broken setup, so there the missing emulator is a failure.
            if (Environment.GetEnvironmentVariable("REQUIRE_COSMOS_EMULATOR") == "true")
            {
                throw new InvalidOperationException("REQUIRE_COSMOS_EMULATOR is set but the Cosmos DB emulator is not reachable.", ex);
            }

            SkipReason = $"Cosmos DB emulator not reachable ({ex.GetType().Name}). Start it with 'docker compose up -d'.";
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public TestDatabase CreateDatabase()
    {
        Assert.SkipUnless(SkipReason is null, SkipReason ?? string.Empty);
        return new TestDatabase(ConnectionString, $"ipl-store-test-{Guid.NewGuid():N}");
    }
}

public sealed class TestDatabase : IAsyncDisposable
{
    private static readonly string[] SystemProperties = ["_rid", "_self", "_etag", "_attachments", "_ts"];

    private readonly ServiceProvider services;

    public TestDatabase(string connectionString, string databaseName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosDb:ConnectionString"] = connectionString,
                ["CosmosDb:DatabaseName"] = databaseName,
            })
            .Build();

        services = new ServiceCollection().AddLogging().AddDbMaintenance(configuration).BuildServiceProvider();
        Name = databaseName;
    }

    public string Name { get; }

    public Database Database => Get<CosmosClient>().GetDatabase(Name);

    public T Get<T>() where T : notnull => services.GetRequiredService<T>();

    public DocumentOperations Operations() =>
        new(Database, Get<IOptions<DbMaintenanceOptions>>().Value, NullLogger<DocumentOperations>.Instance);

    public MigrationRunner Runner(params Migration[] migrations) =>
        new(Get<CosmosClient>(), Get<IOptions<DbMaintenanceOptions>>(), migrations, TimeProvider.System, Get<ILoggerFactory>());

    public Task<JsonObject?> ReadAsync(string container, string id, string partitionKey) => ReadAsync(container, id, new PartitionKey(partitionKey));

    public async Task<JsonObject?> ReadAsync(string container, string id, PartitionKey partitionKey)
    {
        using var response = await Database.GetContainer(container).ReadItemStreamAsync(id, partitionKey);
        return response.IsSuccessStatusCode ? (JsonObject)JsonNode.Parse(response.Content)! : null;
    }

    /// <summary>Every document in every declared container, without system properties, in a stable order.</summary>
    public async Task<IReadOnlyList<string>> SnapshotAsync()
    {
        var documents = new List<string>();

        foreach (var spec in SchemaSpec.Current.Containers)
        {
            using var iterator = Database.GetContainer(spec.Name).GetItemQueryIterator<JsonObject>("SELECT * FROM c");
            while (iterator.HasMoreResults)
            {
                foreach (var document in await iterator.ReadNextAsync())
                {
                    foreach (var property in SystemProperties)
                    {
                        document.Remove(property);
                    }

                    documents.Add($"{spec.Name}: {document.ToJsonString()}");
                }
            }
        }

        return documents.Order(StringComparer.Ordinal).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Database.DeleteAsync();
        }
        catch (CosmosException)
        {
            // Never created, or already gone.
        }

        await services.DisposeAsync();
    }
}
