using IplFranchise.Application;
using IplFranchise.Domain;
using IplFranchise.Infrastructure.Documents;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

internal sealed class ProductStore(CosmosContainers containers) : IProductStore
{
    public async Task<RawPage<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken)
    {
        var where = "p.docKind = 'product'";
        var parameters = new List<(string Name, object Value)>();

        if (search.Type is not null)
        {
            where += " AND p.type = @type";
            parameters.Add(("@type", search.Type));
        }

        if (search.Text is not null)
        {
            where += " AND CONTAINS(p.nameLower, @text)";
            parameters.Add(("@text", search.Text));
        }

        // One franchise is one partition; without it the query fans out, which the composite index keeps affordable.
        PartitionKey? key = search.FranchiseId is null ? null : new PartitionKey(search.FranchiseId);

        var page = await CosmosHelpers.ReadSeekPageAsync<ProductDoc>(
            containers.Products,
            new SeekQuery("p", where, parameters, SortField: "nameLower", Descending: false),
            key,
            search.PageSize,
            search.Token,
            doc => new SeekKey(doc.NameLower, doc.Id),
            cancellationToken);

        int? total = null;
        if (search.WithTotal)
        {
            var count = new QueryDefinition($"SELECT VALUE COUNT(1) FROM p WHERE {where}");
            foreach (var (name, value) in parameters)
            {
                count = count.WithParameter(name, value);
            }

            total = await CosmosHelpers.CountAsync(containers.Products, count, key, cancellationToken);
        }

        return new RawPage<Product>(page.Items.Select(d => d.ToDomain()).ToList(), page.NextToken, total);
    }

    public async Task<Versioned<Product>?> GetAsync(string franchiseId, string productId, CancellationToken cancellationToken)
    {
        var found = await CosmosHelpers.TryReadAsync<ProductDoc>(
            containers.Products, productId, new PartitionKey(franchiseId), cancellationToken);

        return found is { Doc.DocKind: "product" } ? new Versioned<Product>(found.Value.Doc.ToDomain(), found.Value.ETag) : null;
    }

    public async Task<IReadOnlyDictionary<string, Product>> GetManyAsync(
        IEnumerable<(string FranchiseId, string ProductId)> keys, CancellationToken cancellationToken)
    {
        var docs = await CosmosHelpers.ReadManyAsync<ProductDoc>(
            containers.Products, keys.Select(k => (k.ProductId, new PartitionKey(k.FranchiseId))), cancellationToken);

        return docs.Where(d => d.DocKind == "product").ToDictionary(d => d.Id, d => d.ToDomain());
    }

    /// <summary>Crosses every franchise partition, but returns only one short string per franchise.</summary>
    public async Task<IReadOnlyList<string>> ListFranchiseIdsAsync(CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT DISTINCT VALUE p.franchiseId FROM p WHERE p.docKind = 'product'");
        using var iterator = containers.Products.GetItemQueryIterator<string>(query);
        var codes = new HashSet<string>(StringComparer.Ordinal);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            codes.UnionWith(response);
        }

        return codes.ToList();
    }
}
