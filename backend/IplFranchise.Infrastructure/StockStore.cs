using IplFranchise.Application;
using IplFranchise.Infrastructure.Documents;

namespace IplFranchise.Infrastructure;

internal sealed class StockStore(CosmosContainers containers) : IStockStore
{
    public async Task<IReadOnlyDictionary<string, int>> GetManyAsync(IEnumerable<string> productIds, CancellationToken cancellationToken)
    {
        var docs = await CosmosHelpers.ReadManyAsync<StockDoc>(
            containers.Inventory, productIds.Select(id => (id, CosmosHelpers.Tenant)), cancellationToken);

        return docs.Where(d => d.DocKind == "stock").ToDictionary(d => d.Id, d => d.Available);
    }

    public async Task<Versioned<int>?> GetAsync(string productId, CancellationToken cancellationToken)
    {
        var found = await CosmosHelpers.TryReadAsync<StockDoc>(containers.Inventory, productId, CosmosHelpers.Tenant, cancellationToken);
        return found is { Doc.DocKind: "stock" } ? new Versioned<int>(found.Value.Doc.Available, found.Value.ETag) : null;
    }
}
