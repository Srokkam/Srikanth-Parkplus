using IplFranchise.Application;
using IplFranchise.Domain;

namespace IplFranchise.UnitTests;

/// <summary>
/// An in-memory stand-in for the database. Each method takes one lock, the way a transactional batch is
/// all-or-nothing in Cosmos DB, so the services above it can be tested without a database.
/// </summary>
internal sealed class FakeStore : IProductStore, IStockStore, ICartStore, ICheckoutStore, IOrderStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, Product> products = [];
    private readonly Dictionary<string, int> stock = [];
    private readonly Dictionary<string, (Cart Cart, int Version)> carts = [];
    private readonly Dictionary<string, IdempotencyRecord> records = [];
    private readonly Dictionary<string, Order> markers = [];
    private readonly HashSet<string> markersWithOrder = [];
    private readonly HashSet<string> purchases = [];
    private readonly Dictionary<string, Order> orders = [];

    public int CommittedCartWrites { get; private set; }

    public int Stock(string productId) => stock[productId];

    public int OrderCount => orders.Count;

    public bool HasCart(string userId) => carts.ContainsKey(userId);

    public void Add(Product product, int available)
    {
        products[product.Id] = product;
        stock[product.Id] = available;
    }

    public void SetPrice(string productId, long pricePaise) => products[productId] = products[productId] with { PricePaise = pricePaise };

    public void SetStock(string productId, int available) => stock[productId] = available;

    public Task<RawPage<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken) =>
        Task.FromResult(new RawPage<Product>(products.Values.OrderBy(p => p.Name).ToList(), null, products.Count));

    public Task<Versioned<Product>?> GetAsync(string franchiseId, string productId, CancellationToken cancellationToken) =>
        Task.FromResult(products.TryGetValue(productId, out var p) ? new Versioned<Product>(p, "etag") : null);

    public Task<IReadOnlyDictionary<string, Product>> GetManyAsync(IEnumerable<(string FranchiseId, string ProductId)> keys, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Product>>(
            keys.Where(k => products.ContainsKey(k.ProductId)).ToDictionary(k => k.ProductId, k => products[k.ProductId]));

    public Task<IReadOnlyList<string>> ListFranchiseIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(products.Values.Select(p => p.FranchiseId).Distinct().ToList());

    public Task<IReadOnlyDictionary<string, int>> GetManyAsync(IEnumerable<string> productIds, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyDictionary<string, int>>(
                productIds.Where(stock.ContainsKey).ToDictionary(id => id, id => stock[id]));
        }
    }

    Task<Versioned<int>?> IStockStore.GetAsync(string productId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(stock.TryGetValue(productId, out var available) ? new Versioned<int>(available, "etag") : null);
        }
    }

    Task<Versioned<Cart>?> ICartStore.GetAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(carts.TryGetValue(userId, out var entry) ? new Versioned<Cart>(entry.Cart, entry.Version.ToString()) : null);
        }
    }

    public Task<IdempotencyRecord?> GetIdempotencyAsync(string userId, string key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(records.GetValueOrDefault($"{userId}/{key}"));
        }
    }

    public Task<CommitResult> CommitAsync(CartCommit commit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (records.ContainsKey($"{commit.UserId}/{commit.Record.Key}"))
            {
                return Task.FromResult(CommitResult.DuplicateRequest);
            }

            var exists = carts.TryGetValue(commit.UserId, out var current);
            if (exists ? current.Version.ToString() != commit.ExpectedETag : commit.ExpectedETag is not null)
            {
                return Task.FromResult(CommitResult.ETagConflict);
            }

            if (commit.NewCart is null)
            {
                carts.Remove(commit.UserId);
            }
            else
            {
                carts[commit.UserId] = (commit.NewCart, current.Version + 1);
            }

            records[$"{commit.UserId}/{commit.Record.Key}"] = commit.Record;
            CommittedCartWrites++;
            return Task.FromResult(CommitResult.Committed);
        }
    }

    public Task ClearAsync(string userId, string eTag, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (carts.TryGetValue(userId, out var entry) && entry.Version.ToString() == eTag)
            {
                carts.Remove(userId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<DeductOutcome> DeductAsync(Order order, IReadOnlySet<string> limitedProductIds, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (markers.ContainsKey(order.Id))
            {
                return Task.FromResult(DeductOutcome.AlreadyDeducted);
            }

            if (limitedProductIds.Any(id => purchases.Contains($"{order.UserId}/{id}")))
            {
                return Task.FromResult(DeductOutcome.LimitReached);
            }

            if (order.Lines.Any(l => stock.GetValueOrDefault(l.ProductId) < l.Quantity))
            {
                return Task.FromResult(DeductOutcome.InsufficientStock);
            }

            foreach (var line in order.Lines)
            {
                stock[line.ProductId] -= line.Quantity;
            }

            foreach (var id in limitedProductIds)
            {
                purchases.Add($"{order.UserId}/{id}");
            }

            markers[order.Id] = order;
            return Task.FromResult(DeductOutcome.Deducted);
        }
    }

    public Task<Order?> GetSnapshotAsync(string orderId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(markers.GetValueOrDefault(orderId));
        }
    }

    public Task MarkOrderCreatedAsync(string orderId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            markersWithOrder.Add(orderId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Order>> FindIncompleteAsync(DateTimeOffset olderThan, int max, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<Order>>(
                markers.Values.Where(o => !markersWithOrder.Contains(o.Id) && o.CreatedUtc < olderThan).Take(max).ToList());
        }
    }

    public Task<IReadOnlySet<string>> GetPurchasedAsync(string userId, IEnumerable<string> productIds, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlySet<string>>(productIds.Where(id => purchases.Contains($"{userId}/{id}")).ToHashSet());
        }
    }

    public Task<PlacedOrder> CreateIfAbsentAsync(Order order, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (orders.TryGetValue(order.Id, out var existing))
            {
                return Task.FromResult(new PlacedOrder(existing, Created: false));
            }

            orders[order.Id] = order;
            return Task.FromResult(new PlacedOrder(order, Created: true));
        }
    }

    Task<Order?> IOrderStore.GetAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(orders.TryGetValue(orderId, out var order) && order.UserId == userId ? order : null);
        }
    }

    public Task<RawPage<Order>> ListAsync(string userId, DateTimeOffset? from, DateTimeOffset? to, int pageSize, string? token, CancellationToken cancellationToken) =>
        Task.FromResult(new RawPage<Order>(orders.Values.Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedUtc).Take(pageSize).ToList(), null));
}

internal sealed class PassThroughTokens : IContinuationTokenProtector
{
    public string Protect(string cosmosToken, string scope) => $"{scope}:{cosmosToken}";

    public string Unprotect(string token, string scope) =>
        token.StartsWith(scope + ":", StringComparison.Ordinal) ? token[(scope.Length + 1)..] : throw new InvalidContinuationTokenException();
}

/// <summary>Records the products it is asked to create; the edit operations aren't needed by these tests.</summary>
internal sealed class FakeAdminStore : ICatalogueAdminStore
{
    public List<(Product Product, int InitialStock, string Actor)> Created { get; } = [];

    public Task<CreatedProduct> CreateProductAsync(
        Product product, int initialStock, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Created.Add((product, initialStock, actor));
        return Task.FromResult(new CreatedProduct(new Versioned<Product>(product, "etag-1"), initialStock));
    }

    public Task<Versioned<Product>> ChangePriceAsync(
        string franchiseId, string productId, long newPricePaise, string eTag, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Versioned<int>> SetStockAsync(
        string productId, int newAvailable, string eTag, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<RawPage<AuditEntry>> ListAuditAsync(
        string field, string franchiseId, string productId, int pageSize, string? token, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
