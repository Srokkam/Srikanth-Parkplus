using System.Net;
using IplFranchise.Application;
using IplFranchise.Domain;
using IplFranchise.Infrastructure.Documents;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

internal sealed class CatalogueAdminStore(CosmosContainers containers) : ICatalogueAdminStore
{
    public async Task<CreatedProduct> CreateProductAsync(
        Product product, int initialStock, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Stock first, with its audit row, in one batch. The two containers can't share a transaction, so if the product
        // write below fails, a retry finds this stock record, keeps it, and finishes by creating the product.
        var stock = new StockDoc { Id = product.Id, ProductId = product.Id, Available = initialStock };
        var audit = new AuditDoc
        {
            Id = $"audit:stock:{product.Id}:{Guid.NewGuid():N}",
            TenantId = Kinds.Tenant,
            ProductId = product.Id,
            Field = "stock",
            OldValue = 0,
            NewValue = initialStock,
            Delta = initialStock,
            Reason = "initial stock",
            Actor = actor,
            TsUtc = Timestamps.ToText(now),
        };

        var stockResponse = await CosmosHelpers.RunAsync(
            containers.Inventory.CreateTransactionalBatch(CosmosHelpers.Tenant).CreateItem(stock).CreateItem(audit), cancellationToken);

        var available = initialStock;
        if (!stockResponse.IsSuccessStatusCode)
        {
            var failed = CosmosHelpers.FailedOperation(stockResponse);
            if (failed.Index != 0 || failed.Status != HttpStatusCode.Conflict)
            {
                throw CosmosHelpers.Unexpected(failed.Status, "creating stock");
            }

            var existing = await CosmosHelpers.TryReadAsync<StockDoc>(containers.Inventory, product.Id, CosmosHelpers.Tenant, cancellationToken);
            available = existing?.Doc.Available ?? initialStock;
        }

        var document = new ProductDoc
        {
            Id = product.Id,
            FranchiseId = product.FranchiseId,
            Type = product.Type,
            Name = product.Name,
            NameLower = product.Name.ToLowerInvariant(),
            Description = product.Description,
            PricePaise = product.PricePaise,
            LimitedEdition = product.LimitedEdition,
            MaxPerCustomer = product.MaxPerCustomer,
        };

        try
        {
            var response = await containers.Products.CreateItemAsync(document, new PartitionKey(product.FranchiseId), cancellationToken: cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            return new CreatedProduct(new Versioned<Product>(response.Resource.ToDomain(), response.ETag), available);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ConflictException(
                ProblemCodes.ProductExists, $"A product with id '{product.Id}' already exists in franchise '{product.FranchiseId}'.");
        }
    }

    public async Task<Versioned<Product>> ChangePriceAsync(
        string franchiseId, string productId, long newPricePaise, string eTag, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = new PartitionKey(franchiseId);
        var current = await CosmosHelpers.TryReadAsync<ProductDoc>(containers.Products, productId, key, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        if (current.ETag != eTag)
        {
            throw new PreconditionFailedException(current.ETag);
        }

        if (current.Doc.PricePaise == newPricePaise)
        {
            return new Versioned<Product>(current.Doc.ToDomain(), current.ETag);
        }

        var audit = new AuditDoc
        {
            Id = $"audit:price:{productId}:{Guid.NewGuid():N}",
            FranchiseId = franchiseId,
            ProductId = productId,
            Field = "price",
            OldValue = current.Doc.PricePaise,
            NewValue = newPricePaise,
            Reason = reason,
            Actor = actor,
            TsUtc = Timestamps.ToText(now),
        };

        var batch = containers.Products.CreateTransactionalBatch(key)
            .ReplaceItem(productId, current.Doc with { PricePaise = newPricePaise, ETag = null }, new TransactionalBatchItemRequestOptions { IfMatchEtag = eTag })
            .CreateItem(audit);

        var response = await CosmosHelpers.RunAsync(batch, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var saved = response.GetOperationResultAtIndex<ProductDoc>(0);
            return new Versioned<Product>(saved.Resource.ToDomain(), saved.ETag);
        }

        var failed = CosmosHelpers.FailedOperation(response);
        if (failed.Index == 0 && failed.Status == HttpStatusCode.PreconditionFailed)
        {
            throw new PreconditionFailedException(await CurrentETagAsync(containers.Products, productId, key, cancellationToken));
        }

        throw CosmosHelpers.Unexpected(failed.Status, "changing a price");
    }

    public async Task<Versioned<int>> SetStockAsync(
        string productId, int newAvailable, string eTag, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await CosmosHelpers.TryReadAsync<StockDoc>(containers.Inventory, productId, CosmosHelpers.Tenant, cancellationToken)
            ?? throw new NotFoundException($"Stock for product '{productId}' was not found.");

        if (current.ETag != eTag)
        {
            throw new PreconditionFailedException(current.ETag);
        }

        if (current.Doc.Available == newAvailable)
        {
            return new Versioned<int>(current.Doc.Available, current.ETag);
        }

        var audit = new AuditDoc
        {
            Id = $"audit:stock:{productId}:{Guid.NewGuid():N}",
            TenantId = Kinds.Tenant,
            ProductId = productId,
            Field = "stock",
            OldValue = current.Doc.Available,
            NewValue = newAvailable,
            Delta = newAvailable - current.Doc.Available,
            Reason = reason,
            Actor = actor,
            TsUtc = Timestamps.ToText(now),
        };

        var batch = containers.Inventory.CreateTransactionalBatch(CosmosHelpers.Tenant)
            .ReplaceItem(productId, current.Doc with { Available = newAvailable, ETag = null }, new TransactionalBatchItemRequestOptions { IfMatchEtag = eTag })
            .CreateItem(audit);

        var response = await CosmosHelpers.RunAsync(batch, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var saved = response.GetOperationResultAtIndex<StockDoc>(0);
            return new Versioned<int>(saved.Resource.Available, saved.ETag);
        }

        var failed = CosmosHelpers.FailedOperation(response);
        if (failed.Index == 0 && failed.Status == HttpStatusCode.PreconditionFailed)
        {
            throw new PreconditionFailedException(await CurrentETagAsync(containers.Inventory, productId, CosmosHelpers.Tenant, cancellationToken));
        }

        throw CosmosHelpers.Unexpected(failed.Status, "setting stock");
    }

    public async Task<RawPage<AuditEntry>> ListAuditAsync(
        string field, string franchiseId, string productId, int pageSize, string? token, CancellationToken cancellationToken)
    {
        var (container, key) = field == "price"
            ? (containers.Products, new PartitionKey(franchiseId))
            : (containers.Inventory, CosmosHelpers.Tenant);

        var query = new QueryDefinition(
                "SELECT * FROM a WHERE a.docKind = 'audit' AND a.productId = @productId AND a.field = @field ORDER BY a.tsUtc DESC")
            .WithParameter("@productId", productId)
            .WithParameter("@field", field);

        var page = await CosmosHelpers.ReadPageAsync<AuditDoc>(container, query, key, pageSize, token, cancellationToken);
        return new RawPage<AuditEntry>(page.Items.Select(d => d.ToDomain()).ToList(), page.NextToken);
    }

    private static async Task<string?> CurrentETagAsync(Container container, string id, PartitionKey key, CancellationToken cancellationToken)
    {
        var latest = await CosmosHelpers.TryReadAsync<ETagOnly>(container, id, key, cancellationToken);
        return latest?.ETag;
    }
}
