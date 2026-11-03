using IplFranchise.Domain;

namespace IplFranchise.Application;

/// <summary>Joins a cart with today's prices and stock. Nothing about price or stock is ever stored in the cart.</summary>
public sealed class CartViewBuilder(IProductStore products, IStockStore stock)
{
    public async Task<CartView> BuildAsync(Cart cart, CancellationToken cancellationToken)
    {
        if (cart.IsEmpty)
        {
            return CartView.Empty;
        }

        var catalogue = await products.GetManyAsync(cart.Lines.Select(l => (l.FranchiseId, l.ProductId)), cancellationToken);
        var available = await stock.GetManyAsync(cart.Lines.Select(l => l.ProductId), cancellationToken);
        return CartPricing.Build(cart, catalogue, available);
    }
}
