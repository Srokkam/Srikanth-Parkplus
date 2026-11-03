using IplFranchise.Domain;

namespace IplFranchise.Application;

public sealed class CheckoutService(
    ICartStore carts,
    CartViewBuilder views,
    IProductStore products,
    IStockStore stock,
    ICheckoutStore checkout,
    IOrderStore orders,
    TimeProvider clock)
{
    public async Task<PlacedOrder> PlaceOrderAsync(string userId, string orderId, long acceptedTotalPaise, CancellationToken cancellationToken)
    {
        if (await FindPlacedAsync(userId, orderId, cancellationToken) is { } existing)
        {
            return existing;
        }

        var cart = await carts.GetAsync(userId, cancellationToken);
        if (cart is null || cart.Value.IsEmpty)
        {
            // The same request may have finished (and emptied the cart) between our first look and this one.
            return await FindPlacedAsync(userId, orderId, cancellationToken)
                ?? throw new ConflictException(ProblemCodes.CartEmpty, "The cart is empty.");
        }

        var view = await views.BuildAsync(cart.Value, cancellationToken);
        switch (CheckoutRules.Decide(view, acceptedTotalPaise))
        {
            case CheckoutDecision.PriceChanged changed:
                Telemetry.CheckoutOutcomes.Add(1, Outcome("price-changed"));
                throw new ConflictException(
                    ProblemCodes.PriceChanged,
                    "Prices or totals changed. Review and accept the new total before checking out.",
                    new Dictionary<string, object?> { ["currentTotalPaise"] = changed.CurrentTotalPaise });

            case CheckoutDecision.Blocked blocked:
                throw OutOfStock(blocked.Lines);
        }

        var order = Order.FromView(orderId, userId, Truncate(clock.GetUtcNow()), view);
        var limited = await LimitedProductIdsAsync(cart.Value, cancellationToken);

        switch (await checkout.DeductAsync(order, limited, cancellationToken))
        {
            case DeductOutcome.AlreadyDeducted:
                order = await SnapshotFromEarlierAttemptAsync(userId, orderId, cancellationToken);
                break;

            case DeductOutcome.InsufficientStock:
                throw OutOfStock(await FailedLinesAsync(order, cancellationToken));

            case DeductOutcome.LimitReached:
                throw LimitReached(await checkout.GetPurchasedAsync(userId, limited, cancellationToken));
        }

        var placed = await orders.CreateIfAbsentAsync(order, cancellationToken);
        await checkout.MarkOrderCreatedAsync(orderId, cancellationToken);
        await carts.ClearAsync(userId, cart.ETag, cancellationToken);

        Telemetry.CheckoutOutcomes.Add(1, Outcome(placed.Created ? "confirmed" : "replayed"));
        return placed;
    }

    private async Task<PlacedOrder?> FindPlacedAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(userId, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        Telemetry.CheckoutOutcomes.Add(1, Outcome("replayed"));
        return new PlacedOrder(order, Created: false);
    }

    private async Task<Order> SnapshotFromEarlierAttemptAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        var snapshot = await checkout.GetSnapshotAsync(orderId, cancellationToken)
            ?? throw new ConflictException(ProblemCodes.CartConflict, "Checkout is already in progress. Please retry.");

        if (snapshot.UserId != userId)
        {
            throw new ConflictException(ProblemCodes.IdempotencyKeyReused, "This order id belongs to a different customer.");
        }

        return snapshot;
    }

    private async Task<IReadOnlySet<string>> LimitedProductIdsAsync(Cart cart, CancellationToken cancellationToken)
    {
        var catalogue = await products.GetManyAsync(cart.Lines.Select(l => (l.FranchiseId, l.ProductId)), cancellationToken);
        return catalogue.Values.Where(p => p.LimitedEdition).Select(p => p.Id).ToHashSet();
    }

    private async Task<IReadOnlyList<FailedLine>> FailedLinesAsync(Order order, CancellationToken cancellationToken)
    {
        var available = await stock.GetManyAsync(order.Lines.Select(l => l.ProductId), cancellationToken);
        return order.Lines
            .Where(l => available.GetValueOrDefault(l.ProductId) < l.Quantity)
            .Select(l => CheckoutRules.ToFailedLine(l.ProductId, l.Quantity, available.GetValueOrDefault(l.ProductId)))
            .ToList();
    }

    private static ConflictException OutOfStock(IReadOnlyList<FailedLine> lines)
    {
        Telemetry.CheckoutOutcomes.Add(1, Outcome("out-of-stock"));

        // Stock may have been topped up between the failed batch and our re-read: let the customer simply retry.
        return lines.Count == 0
            ? new ConflictException(ProblemCodes.CartConflict, "Stock changed while the order was placed. Please try again.")
            : new ConflictException(
                ProblemCodes.OutOfStock,
                "Some items cannot be fulfilled. No stock was deducted and your cart is unchanged.",
                new Dictionary<string, object?> { ["failedLines"] = lines });
    }

    private static ConflictException LimitReached(IReadOnlySet<string> alreadyBought)
    {
        Telemetry.CheckoutOutcomes.Add(1, Outcome("limit-reached"));
        var lines = alreadyBought.Select(id => new FailedLine(id, 0, 0, FailedLine.LimitExceeded)).ToList();
        return new ConflictException(
            ProblemCodes.QuantityLimitExceeded,
            "A limited edition item in the cart was already bought by this customer.",
            new Dictionary<string, object?> { ["failedLines"] = lines });
    }

    private static DateTimeOffset Truncate(DateTimeOffset value) => Timestamps.Parse(Timestamps.ToText(value));

    private static KeyValuePair<string, object?> Outcome(string result) => new("result", result);
}
