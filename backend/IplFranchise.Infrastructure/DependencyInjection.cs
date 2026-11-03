using System.Data.Common;
using System.Text.Json;
using Azure.Identity;
using IplFranchise.Application;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IplFranchise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CosmosDbOptions>()
            .Bind(configuration.GetSection(CosmosDbOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ContinuationTokenOptions>()
            .Bind(configuration.GetSection(ContinuationTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => CreateClient(sp.GetRequiredService<IOptions<CosmosDbOptions>>().Value));
        services.AddSingleton(sp => new CosmosContainers(
            sp.GetRequiredService<CosmosClient>(), sp.GetRequiredService<IOptions<CosmosDbOptions>>().Value.DatabaseName));

        services.AddSingleton<IContinuationTokenProtector, ContinuationTokenProtector>();
        services.AddSingleton<IProductStore, ProductStore>();
        services.AddSingleton<IStockStore, StockStore>();
        services.AddSingleton<ICatalogueAdminStore, CatalogueAdminStore>();
        services.AddSingleton<ICartStore, CartStore>();
        services.AddSingleton<ICheckoutStore, CheckoutStore>();
        services.AddSingleton<IOrderStore, OrderStore>();

        return services;
    }

    /// <summary>One client for the whole process: it is thread-safe, manages its connections and holds the session token.</summary>
    /// <summary>The Cosmos DB emulator's published account key. Real accounts never use it.</summary>
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static CosmosClient CreateClient(CosmosDbOptions options)
    {
        var connectionString = options.ConnectionString;
        var settings = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var endpoint = settings.TryGetValue("AccountEndpoint", out var value) && Uri.TryCreate(value?.ToString(), UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException("CosmosDb:ConnectionString must contain a valid AccountEndpoint.");

        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "IplFranchise.Api",
            // camelCase property names, so a C# "Id" property maps to the required Cosmos "id" field.
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            RequestTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            MaxRetryAttemptsOnRateLimitedRequests = options.MaxRetriesOnThrottling,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(options.MaxRetryWaitSeconds),
        };

        var key = settings.TryGetValue("AccountKey", out var accountKey) ? accountKey?.ToString() : null;

        // The emulator: on localhost, or under another host name inside Docker Compose (recognised by its public key).
        if (endpoint.IsLoopback || key == EmulatorKey)
        {
            // The local emulator only supports Gateway mode in Docker and uses a self-signed certificate in https mode.
            clientOptions.ConnectionMode = ConnectionMode.Gateway;
            clientOptions.LimitToEndpoint = true;
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        // No AccountKey means Entra ID: real accounts run with key authentication disabled.
        return !string.IsNullOrWhiteSpace(key)
            ? new CosmosClient(connectionString, clientOptions)
            : new CosmosClient(endpoint.ToString(), new DefaultAzureCredential(), clientOptions);
    }
}
