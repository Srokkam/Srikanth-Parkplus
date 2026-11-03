using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

/// <summary>The containers created by IplFranchise.DbMaintenance. Names match Schema/schema.json.</summary>
internal sealed class CosmosContainers(CosmosClient client, string databaseName)
{
    public Container Products { get; } = client.GetContainer(databaseName, "products");

    public Container Inventory { get; } = client.GetContainer(databaseName, "inventory");

    public Container Carts { get; } = client.GetContainer(databaseName, "carts");

    public Container Orders { get; } = client.GetContainer(databaseName, "orders");
}
