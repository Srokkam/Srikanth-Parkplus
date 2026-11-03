using System.Net;
using IplFranchise.DbMaintenance.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IplFranchise.DbMaintenance.Schema;

/// <summary>
/// Reads every container's settings and compares them with Schema/schema.json.
/// Run before any data migration and after every deployment.
/// </summary>
public sealed class SchemaVerifier(CosmosClient client, IOptions<DbMaintenanceOptions> options, ILogger<SchemaVerifier> logger)
{
    public async Task<IReadOnlyList<string>> VerifyAsync(CancellationToken cancellationToken)
    {
        var databaseName = options.Value.DatabaseName;
        var snapshots = new List<ContainerSnapshot>();

        try
        {
            using var iterator = client.GetDatabase(databaseName).GetContainerQueryIterator<ContainerProperties>();
            while (iterator.HasMoreResults)
            {
                foreach (var properties in await iterator.ReadNextAsync(cancellationToken))
                {
                    snapshots.Add(ContainerPropertiesMapper.ToSnapshot(properties));
                }
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var missing = new[] { $"Database '{databaseName}' does not exist." };
            logger.LogError("Schema drift: {Problem}", missing[0]);
            return missing;
        }

        var drift = SchemaComparer.Compare(SchemaSpec.Current with { DatabaseName = databaseName }, snapshots);
        foreach (var problem in drift)
        {
            logger.LogError("Schema drift: {Problem}", problem);
        }

        if (drift.Count == 0)
        {
            logger.LogInformation("Schema of '{Database}' matches Schema/schema.json ({Count} containers)", databaseName, snapshots.Count);
        }

        return drift;
    }
}
