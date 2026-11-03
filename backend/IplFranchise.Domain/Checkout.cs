namespace IplFranchise.Domain;

public sealed record FailedLine(string ProductId, int Requested, int Available, string Reason)
{
    public const string OutOfStock = "out-of-stock";
    public const string InsufficientStock = "insufficient-stock";
    public const string ProductUnavailable = "product-unavailable";
    public const string LimitExceeded = "limit-exceeded";
}

public abstract record CheckoutDecision
{
    public sealed record Proceed : CheckoutDecision;

    public sealed record PriceChanged(long CurrentTotalPaise) : CheckoutDecision;

    public sealed record Blocked(IReadOnlyList<FailedLine> Lines) : CheckoutDecision;
}

public static class CheckoutRules
{
    public static CheckoutDecision Decide(CartView view, long acceptedTotalPaise)
    {
        if (view.RequiresAcceptance || acceptedTotalPaise != view.TotalPaise)
        {
            return new CheckoutDecision.PriceChanged(view.TotalPaise);
        }

        var failed = view.Lines
            .Where(l => l.Stock != LineStock.Ok)
            .Select(l => ToFailedLine(l.ProductId, l.Quantity, l.Available))
            .ToList();

        return failed.Count > 0 ? new CheckoutDecision.Blocked(failed) : new CheckoutDecision.Proceed();
    }

    public static FailedLine ToFailedLine(string productId, int requested, int available) =>
        available <= 0
            ? new FailedLine(productId, requested, 0, FailedLine.OutOfStock)
            : new FailedLine(productId, requested, available, FailedLine.InsufficientStock);
}
