using System.Net;
using IplFranchise.Application;
using IplFranchise.Domain;
using IplFranchise.Infrastructure.Documents;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

internal sealed class OrderStore(CosmosContainers containers) : IOrderStore
{
    public async Task<PlacedOrder> CreateIfAbsentAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var response = await containers.Orders.CreateItemAsync(order.ToDocument(), new PartitionKey(order.UserId), cancellationToken: cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            return new PlacedOrder(order, Created: true);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await GetAsync(order.UserId, order.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Order '{order.Id}' reported as existing but could not be read.");
            return new PlacedOrder(existing, Created: false);
        }
    }

    public async Task<Order?> GetAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        var found = await CosmosHelpers.TryReadAsync<OrderDoc>(containers.Orders, orderId, new PartitionKey(userId), cancellationToken);
        return found?.Doc.ToDomain();
    }

    public async Task<RawPage<Order>> ListAsync(
        string userId, DateTimeOffset? from, DateTimeOffset? to, int pageSize, string? token, CancellationToken cancellationToken)
    {
        var where = "o.docKind = 'order'";
        var parameters = new List<(string Name, object Value)>();

        if (from is { } start)
        {
            where += " AND o.createdUtc >= @from";
            parameters.Add(("@from", Timestamps.ToText(start)));
        }

        if (to is { } end)
        {
            where += " AND o.createdUtc < @to";
            parameters.Add(("@to", Timestamps.ToText(end)));
        }

        // Newest first, within the customer's own partition (served by the createdUtc DESC, id DESC composite index).
        var page = await CosmosHelpers.ReadSeekPageAsync<OrderDoc>(
            containers.Orders,
            new SeekQuery("o", where, parameters, SortField: "createdUtc", Descending: true),
            new PartitionKey(userId),
            pageSize,
            token,
            doc => new SeekKey(doc.CreatedUtc, doc.Id),
            cancellationToken);

        return new RawPage<Order>(page.Items.Select(d => d.ToDomain()).ToList(), page.NextToken);
    }
}
