using System.Net;
using IplFranchise.Infrastructure;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace IplFranchise.Api.HealthChecks;

/// <summary>
/// Verifies the API can reach Cosmos DB and that the configured database exists.
/// </summary>
public sealed class CosmosDbHealthCheck(CosmosClient cosmosClient, IOptions<CosmosDbOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var databaseName = options.Value.DatabaseName;

        try
        {
            await cosmosClient.GetDatabase(databaseName).ReadAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy($"Connected to Cosmos DB database '{databaseName}'.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Cosmos DB is reachable but database '{databaseName}' does not exist. Run IplFranchise.DbMaintenance setup-local (emulator) or deploy its Infrastructure/main.bicep.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Unable to connect to Cosmos DB.", ex);
        }
    }
}
