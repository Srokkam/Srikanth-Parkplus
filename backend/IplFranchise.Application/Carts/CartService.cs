using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IplFranchise.Domain;

namespace IplFranchise.Application;

public sealed record AddCartItem(string FranchiseId, string ProductId, int Quantity);

public sealed class CartService(
    ICartStore carts,
    IProductStore products,
    IStockStore stock,
    CartViewBuilder views)
{
    private const int Attempts = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CartView> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var cart = await carts.GetAsync(userId, cancellationToken);
        return await views.BuildAsync(cart?.Value ?? Cart.Empty(userId), cancellationToken);
    }

    public Task<CartView> AddAsync(string userId, string requestId, AddCartItem item, CancellationToken cancellationToken)
    {
        ValidateQuantity(item.Quantity);
        return ApplyAsync(
            userId, requestId, Fingerprint("add", item.FranchiseId, item.ProductId, item.Quantity),
            async (cart, ct) =>
            {
                var product = await RequireProductAsync(item.FranchiseId, item.ProductId, ct);
                var existing = cart.Find(item.ProductId);
                return await WithQuantityAsync(cart, product, (existing?.Quantity ?? 0) + item.Quantity, existing, ct);
            },
            cancellationToken);
    }

    public Task<CartView> SetQuantityAsync(string userId, string requestId, string productId, int quantity, CancellationToken cancellationToken)
    {
        ValidateQuantity(quantity);
        return ApplyAsync(
            userId, requestId, Fingerprint("set", productId, quantity),
            async (cart, ct) =>
            {
                var existing = cart.Find(productId)
                    ?? throw new NotFoundException($"Product '{productId}' is not in the cart.");
                var product = await RequireProductAsync(existing.FranchiseId, productId, ct);
                return await WithQuantityAsync(cart, product, quantity, existing, ct);
            },
            cancellationToken);
    }

    public Task<CartView> RemoveAsync(string userId, string requestId, string productId, CancellationToken cancellationToken) =>
        ApplyAsync(userId, requestId, Fingerprint("remove", productId), (cart, _) => Task.FromResult(cart.Without(productId)), cancellationToken);

    /// <summary>The customer agrees to today's prices. Refused if the total moved again since they looked.</summary>
    public Task<CartView> AcceptChangesAsync(string userId, string requestId, long expectedTotalPaise, CancellationToken cancellationToken) =>
        ApplyAsync(
            userId, requestId, Fingerprint("accept", expectedTotalPaise),
            async (cart, ct) =>
            {
                var view = await views.BuildAsync(cart, ct);
                if (view.TotalPaise != expectedTotalPaise)
                {
                    throw new ConflictException(
                        ProblemCodes.PriceChanged,
                        "The total changed again. Review the cart before accepting.",
                        new Dictionary<string, object?> { ["currentTotalPaise"] = view.TotalPaise });
                }

                var current = view.Lines.ToDictionary(l => l.ProductId, l => l.CurrentUnitPricePaise);
                return cart with { Lines = cart.Lines.Select(l => l with { AcceptedUnitPricePaise = current[l.ProductId] }).ToList() };
            },
            cancellationToken);

    private async Task<CartView> ApplyAsync(
        string userId,
        string requestId,
        string fingerprint,
        Func<Cart, CancellationToken, Task<Cart>> change,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (await FindReplayAsync(userId, requestId, fingerprint, cancellationToken) is { } replay)
            {
                return replay;
            }

            var current = await carts.GetAsync(userId, cancellationToken);
            var updated = await change(current?.Value ?? Cart.Empty(userId), cancellationToken);
            var view = await views.BuildAsync(updated, cancellationToken);

            var commit = new CartCommit(
                userId,
                updated.IsEmpty ? null : updated,
                current?.ETag,
                new IdempotencyRecord(requestId, fingerprint, JsonSerializer.Serialize(view, Json)));

            if (await carts.CommitAsync(commit, cancellationToken) == CommitResult.Committed)
            {
                return view;
            }
        }

        throw new ConflictException(ProblemCodes.CartConflict, "The cart was changed by another request. Please try again.");
    }

    private async Task<CartView?> FindReplayAsync(string userId, string requestId, string fingerprint, CancellationToken cancellationToken)
    {
        var saved = await carts.GetIdempotencyAsync(userId, requestId, cancellationToken);
        if (saved is null)
        {
            return null;
        }

        if (saved.Fingerprint != fingerprint)
        {
            throw new ConflictException(ProblemCodes.IdempotencyKeyReused, "This request id was already used for a different request.");
        }

        Telemetry.IdempotentReplays.Add(1);
        return JsonSerializer.Deserialize<CartView>(saved.ResponseJson, Json);
    }

    private async Task<Product> RequireProductAsync(string franchiseId, string productId, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(franchiseId, productId, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");
        return product.Value;
    }

    private async Task<Cart> WithQuantityAsync(Cart cart, Product product, int quantity, CartLine? existing, CancellationToken cancellationToken)
    {
        var available = (await stock.GetAsync(product.Id, cancellationToken))?.Value ?? 0;
        if (CartRules.Check(cart, product, quantity, available) is { } violation)
        {
            throw ToConflict(violation);
        }

        // A line keeps the price the customer accepted, so a price change shows up as a flag instead of slipping in.
        return cart.WithLine(new CartLine(product.Id, product.FranchiseId, quantity, existing?.AcceptedUnitPricePaise ?? product.PricePaise));
    }

    private static ConflictException ToConflict(RuleViolation violation) =>
        new(
            violation.Code,
            violation.Detail,
            new Dictionary<string, object?>
            {
                ["limit"] = violation.Limit,
                ["available"] = violation.Available,
            }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value));

    private static void ValidateQuantity(int quantity)
    {
        if (quantity is < Limits.MinQuantity or > Limits.MaxQuantityPerLine)
        {
            throw new ValidationFailedException(
                "quantity", $"Quantity must be between {Limits.MinQuantity} and {Limits.MaxQuantityPerLine}.");
        }
    }

    private static string Fingerprint(params object[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts))));
}
