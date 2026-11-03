using System.Data.Common;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Core;

public static class CosmosClientFactory
{
    /// <summary>The Cosmos DB emulator's published account key. Real accounts never use it.</summary>
    public const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public static CosmosClient Create(string connectionString)
    {
        var endpoint = GetEndpoint(connectionString);
        var clientOptions = new CosmosClientOptions
        {
            ApplicationName = "IplFranchise.DbMaintenance",
            // Same serializer settings as the API: camelCase, so a C# "Id" property maps to the required Cosmos "id".
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
        };

        if (IsLocalEndpoint(connectionString))
        {
            // The local emulator only supports Gateway mode in Docker and uses a self-signed certificate in https mode.
            clientOptions.ConnectionMode = ConnectionMode.Gateway;
            clientOptions.LimitToEndpoint = true;
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        // No key in the connection string means Entra ID: real accounts run with local (key) auth disabled.
        return HasAccountKey(connectionString)
            ? new CosmosClient(connectionString, clientOptions)
            : new CosmosClient(endpoint.ToString(), new DefaultAzureCredential(), clientOptions);
    }

    /// <summary>
    /// True only for the local emulator: a loopback endpoint, or another host name (such as "cosmos" inside Docker Compose)
    /// used with the emulator's published key, which no real account has.
    /// </summary>
    public static bool IsLocalEndpoint(string connectionString) =>
        GetEndpoint(connectionString).IsLoopback || AccountKey(connectionString) == EmulatorKey;

    private static Uri GetEndpoint(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return builder.TryGetValue("AccountEndpoint", out var value)
            && Uri.TryCreate(value?.ToString(), UriKind.Absolute, out var endpoint)
                ? endpoint
                : throw new InvalidOperationException("CosmosDb:ConnectionString must contain a valid AccountEndpoint.");
    }

    private static bool HasAccountKey(string connectionString) => !string.IsNullOrWhiteSpace(AccountKey(connectionString));

    private static string? AccountKey(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return builder.TryGetValue("AccountKey", out var key) ? key?.ToString() : null;
    }
}
