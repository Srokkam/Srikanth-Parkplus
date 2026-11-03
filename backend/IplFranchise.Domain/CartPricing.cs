namespace IplFranchise.Domain;

public enum LineStock
{
    Ok,
    Insufficient,
    Unavailable,
}

public sealed record CartViewLine(
    string ProductId,
    string FranchiseId,
    string Name,
    int Quantity,
    long AcceptedUnitPricePaise,
    long CurrentUnitPricePaise,
    long LineTotalPaise,
    bool PriceChanged,
    LineStock Stock,
    int Available);

public sealed record CartView(
    IReadOnlyList<CartViewLine> Lines,
    long SubtotalPaise,
    long AcceptedSubtotalPaise,
    long ShippingPaise,
    long TotalPaise,
    bool RequiresAcceptance,
    bool HasBlockingIssues)
{
    public static CartView Empty { get; } = new([], 0, 0, 0, 0, false, false);
}

public static class CartPricing
{
    /// <summary>Shipping is not charged in this version.</summary>
    public const long ShippingPaise = 0;

    public static CartView Build(
        Cart cart,
        IReadOnlyDictionary<string, Product> products,
        IReadOnlyDictionary<string, int> stock)
    {
        var lines = cart.Lines.Select(line => ToViewLine(line, products, stock)).ToList();
        var subtotal = lines.Sum(l => l.LineTotalPaise);
        var accepted = lines.Sum(l => checked(l.AcceptedUnitPricePaise * l.Quantity));
        var shipping = lines.Count == 0 ? 0 : ShippingPaise;

        return new CartView(
            lines,
            subtotal,
            accepted,
            shipping,
            subtotal + shipping,
            RequiresAcceptance: lines.Any(l => l.PriceChanged),
            HasBlockingIssues: lines.Any(l => l.Stock != LineStock.Ok));
    }

    private static CartViewLine ToViewLine(
        CartLine line,
        IReadOnlyDictionary<string, Product> products,
        IReadOnlyDictionary<string, int> stock)
    {
        var found = products.TryGetValue(line.ProductId, out var product);
        var current = found ? product!.PricePaise : line.AcceptedUnitPricePaise;
        var available = stock.GetValueOrDefault(line.ProductId);

        var state = !found || available <= 0 ? LineStock.Unavailable
            : available < line.Quantity ? LineStock.Insufficient
            : LineStock.Ok;

        return new CartViewLine(
            line.ProductId,
            line.FranchiseId,
            found ? product!.Name : line.ProductId,
            line.Quantity,
            line.AcceptedUnitPricePaise,
            current,
            checked(current * line.Quantity),
            current != line.AcceptedUnitPricePaise,
            state,
            available);
    }
}
