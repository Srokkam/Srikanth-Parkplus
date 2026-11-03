namespace IplFranchise.Application;

/// <summary>
/// Finishes checkouts that deducted stock but never created their order (the process stopped between the two steps).
/// The deduct marker holds the whole order, so no information is lost.
/// </summary>
public sealed class CheckoutRecovery(ICheckoutStore checkout, IOrderStore orders, TimeProvider clock)
{
    private const int BatchSize = 50;

    public async Task<int> CompleteStaleAsync(TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        var stale = await checkout.FindIncompleteAsync(clock.GetUtcNow() - gracePeriod, BatchSize, cancellationToken);

        foreach (var order in stale)
        {
            await orders.CreateIfAbsentAsync(order, cancellationToken);
            await checkout.MarkOrderCreatedAsync(order.Id, cancellationToken);
            Telemetry.SweptOrders.Add(1);
        }

        return stale.Count;
    }
}
