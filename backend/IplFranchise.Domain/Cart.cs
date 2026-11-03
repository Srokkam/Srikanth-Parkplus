namespace IplFranchise.Domain;

public sealed record CartLine(string ProductId, string FranchiseId, int Quantity, long AcceptedUnitPricePaise);

public sealed record Cart(string UserId, IReadOnlyList<CartLine> Lines)
{
    public static Cart Empty(string userId) => new(userId, []);

    public bool IsEmpty => Lines.Count == 0;

    public CartLine? Find(string productId) => Lines.FirstOrDefault(l => l.ProductId == productId);

    public Cart WithLine(CartLine line)
    {
        var lines = Lines.ToList();
        var index = lines.FindIndex(l => l.ProductId == line.ProductId);
        if (index >= 0)
        {
            lines[index] = line;
        }
        else
        {
            lines.Add(line);
        }

        return this with { Lines = lines };
    }

    public Cart Without(string productId) =>
        this with { Lines = Lines.Where(l => l.ProductId != productId).ToList() };
}
