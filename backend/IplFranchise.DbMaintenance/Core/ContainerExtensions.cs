using System.Net;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Core;

public static class ContainerExtensions
{
    /// <summary>
    /// Creates the item unless one with the same id already exists in that partition, which is left untouched.
    /// The 409 on conflict is the cheapest correct duplicate guard.
    /// </summary>
    /// <returns>True if the item was created.</returns>
    public static async Task<bool> CreateIfAbsentAsync<T>(
        this Container container,
        T item,
        PartitionKey partitionKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await container.CreateItemAsync(
                item, partitionKey, new ItemRequestOptions { EnableContentResponseOnWrite = false }, cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }
}
