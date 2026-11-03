namespace IplFranchise.Domain;

public sealed record OrderLine(string ProductId, string FranchiseId, string Name, long UnitPricePaise, int Quantity)
{
    public long LineTotalPaise => checked(UnitPricePaise * Quantity);
}

public sealed record Order(
    string Id,
    string UserId,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<OrderLine> Lines,
    long ShippingPaise)
{
    public long SubtotalPaise => Lines.Sum(l => l.LineTotalPaise);

    public long TotalPaise => SubtotalPaise + ShippingPaise;

    /// <summary>Freezes the name and price the customer was shown, so later price changes never alter the order.</summary>
    public static Order FromView(string orderId, string userId, DateTimeOffset now, CartView view) =>
        new(
            orderId,
            userId,
            now,
            view.Lines.Select(l => new OrderLine(l.ProductId, l.FranchiseId, l.Name, l.CurrentUnitPricePaise, l.Quantity)).ToList(),
            view.ShippingPaise);
}
