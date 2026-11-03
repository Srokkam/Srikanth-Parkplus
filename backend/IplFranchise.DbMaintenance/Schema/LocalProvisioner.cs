using System.Net;
using IplFranchise.DbMaintenance.Configuration;
using IplFranchise.DbMaintenance.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IplFranchise.DbMaintenance.Schema;

/// <summary>
/// Local emulator only: creates the database and any missing containers from Schema/schema.json.
/// An existing container is never modified, even if it differs; verify-schema reports that as drift.
/// </summary>
public sealed class LocalProvisioner(CosmosClient client, IOptions<DbMaintenanceOptions> options, ILogger<LocalProvisioner> logger)
{
    /// <returns>The number of resources (database and containers) created.</returns>
    public async Task<int> ProvisionAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        LocalOnly.EnsureLocalEndpoint(settings.ConnectionString, "provision");

        var created = 0;
        var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(settings.DatabaseName, cancellationToken: cancellationToken);
        if (databaseResponse.StatusCode == HttpStatusCode.Created)
        {
            logger.LogInformation("Created database '{Database}'", settings.DatabaseName);
            created++;
        }

        foreach (var spec in SchemaSpec.Current.Containers)
        {
            try
            {
                await databaseResponse.Database.GetContainer(spec.Name).ReadContainerAsync(cancellationToken: cancellationToken);
                logger.LogInformation("Container '{Container}' already exists, left unchanged", spec.Name);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await databaseResponse.Database.CreateContainerAsync(ContainerPropertiesMapper.ToProperties(spec), cancellationToken: cancellationToken);
                logger.LogInformation("Created container '{Container}' (partition key {PartitionKey})", spec.Name, spec.PartitionKeyPath);
                created++;
            }
        }

        return created;
    }
}
