using System.Net;
using IplFranchise.Application;
using IplFranchise.Domain;
using IplFranchise.Infrastructure.Documents;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

internal sealed class CheckoutStore(CosmosContainers containers) : ICheckoutStore
{
    public async Task<DeductOutcome> DeductAsync(Order order, IReadOnlySet<string> limitedProductIds, CancellationToken cancellationToken)
    {
        var batch = containers.Inventory.CreateTransactionalBatch(CosmosHelpers.Tenant);
        var createdUtc = Timestamps.ToText(order.CreatedUtc);

        // The marker is operation 0 on purpose. A retry of a checkout that already went through then fails here,
        // before the stock checks, so it is reported as a repeat even if the shelf has since been emptied.
        batch.CreateItem(new DeductDoc
        {
            Id = MarkerId(order.Id),
            OrderId = order.Id,
            UserId = order.UserId,
            CreatedUtc = createdUtc,
            Order = order.ToDocument(),
        });

        var purchaseIndexes = new Dictionary<int, string>();
        var operations = 1;
        foreach (var line in order.Lines.Where(l => limitedProductIds.Contains(l.ProductId)))
        {
            batch.CreateItem(new PurchaseDoc
            {
                Id = PurchaseId(order.UserId, line.ProductId),
                UserId = order.UserId,
                ProductId = line.ProductId,
                OrderId = order.Id,
            });
            purchaseIndexes[operations++] = line.ProductId;
        }

        // Fixed order, so two checkouts touching the same products always take the locks the same way.
        var deductions = order.Lines.OrderBy(l => l.ProductId, StringComparer.Ordinal).ToList();
        var firstDeduction = operations;
        foreach (var line in deductions)
        {
            batch.PatchItem(
                line.ProductId,
                [PatchOperation.Increment("/available", -line.Quantity)],
                new TransactionalBatchPatchItemRequestOptions { FilterPredicate = $"from c where c.available >= {line.Quantity}" });
            operations++;
        }

        foreach (var line in deductions)
        {
            batch.CreateItem(new AuditDoc
            {
                Id = $"audit:order:{order.Id}:{line.ProductId}",
                TenantId = Kinds.Tenant,
                ProductId = line.ProductId,
                Field = "stock",
                Delta = -line.Quantity,
                Reason = $"order:{order.Id}",
                Actor = order.UserId,
                TsUtc = createdUtc,
            });
        }

        var response = await CosmosHelpers.RunAsync(batch, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return DeductOutcome.Deducted;
        }

        var (index, status) = CosmosHelpers.FailedOperation(response);
        if (index == 0 && status == HttpStatusCode.Conflict)
        {
            return DeductOutcome.AlreadyDeducted;
        }

        if (purchaseIndexes.ContainsKey(index) && status == HttpStatusCode.Conflict)
        {
            return DeductOutcome.LimitReached;
        }

        if (index >= firstDeduction && index < firstDeduction + deductions.Count
            && status is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return DeductOutcome.InsufficientStock;
        }

        throw CosmosHelpers.Unexpected(status, "deducting stock");
    }

    public async Task<Order?> GetSnapshotAsync(string orderId, CancellationToken cancellationToken)
    {
        var marker = await CosmosHelpers.TryReadAsync<DeductDoc>(containers.Inventory, MarkerId(orderId), CosmosHelpers.Tenant, cancellationToken);
        return marker?.Doc.Order.ToDomain();
    }

    public async Task MarkOrderCreatedAsync(string orderId, CancellationToken cancellationToken)
    {
        try
        {
            await containers.Inventory.PatchItemAsync<DeductDoc>(
                MarkerId(orderId),
                CosmosHelpers.Tenant,
                [PatchOperation.Set("/orderCreated", true)],
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    public async Task<IReadOnlyList<Order>> FindIncompleteAsync(DateTimeOffset olderThan, int max, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.docKind = 'deduct' AND c.orderCreated = false AND c.createdUtc < @before")
            .WithParameter("@before", Timestamps.ToText(olderThan));

        var page = await CosmosHelpers.ReadPageAsync<DeductDoc>(containers.Inventory, query, CosmosHelpers.Tenant, max, null, cancellationToken);
        return page.Items.Select(d => d.Order.ToDomain()).ToList();
    }

    public async Task<IReadOnlySet<string>> GetPurchasedAsync(string userId, IEnumerable<string> productIds, CancellationToken cancellationToken)
    {
        var docs = await CosmosHelpers.ReadManyAsync<PurchaseDoc>(
            containers.Inventory, productIds.Select(id => (PurchaseId(userId, id), CosmosHelpers.Tenant)), cancellationToken);

        return docs.Select(d => d.ProductId).ToHashSet();
    }

    private static string MarkerId(string orderId) => $"deduct:{orderId}";

    private static string PurchaseId(string userId, string productId) => $"purchase:{userId}:{productId}";
}
