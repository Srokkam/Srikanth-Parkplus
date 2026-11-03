using IplFranchise.Domain;

namespace IplFranchise.Application;

public interface IProductStore
{
    Task<RawPage<Product>> SearchAsync(ProductSearch search, CancellationToken cancellationToken);

    Task<Versioned<Product>?> GetAsync(string franchiseId, string productId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, Product>> GetManyAsync(IEnumerable<(string FranchiseId, string ProductId)> keys, CancellationToken cancellationToken);

    /// <summary>The distinct franchise codes used by products in the catalogue.</summary>
    Task<IReadOnlyList<string>> ListFranchiseIdsAsync(CancellationToken cancellationToken);
}

public interface IStockStore
{
    Task<IReadOnlyDictionary<string, int>> GetManyAsync(IEnumerable<string> productIds, CancellationToken cancellationToken);

    Task<Versioned<int>?> GetAsync(string productId, CancellationToken cancellationToken);
}

public interface ICatalogueAdminStore
{
    /// <summary>
    /// Creates the stock record (with an audit row) and then the product. An existing stock record is kept, so retrying
    /// after a failure part-way finishes the job.
    /// </summary>
    /// <exception cref="ConflictException">A product with this id already exists in the franchise.</exception>
    Task<CreatedProduct> CreateProductAsync(
        Product product, int initialStock, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Versioned<Product>> ChangePriceAsync(
        string franchiseId, string productId, long newPricePaise, string eTag, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Versioned<int>> SetStockAsync(
        string productId, int newAvailable, string eTag, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<RawPage<AuditEntry>> ListAuditAsync(
        string field, string franchiseId, string productId, int pageSize, string? token, CancellationToken cancellationToken);
}

public interface ICartStore
{
    Task<Versioned<Cart>?> GetAsync(string userId, CancellationToken cancellationToken);

    Task<IdempotencyRecord?> GetIdempotencyAsync(string userId, string key, CancellationToken cancellationToken);

    /// <summary>Writes the cart change and the idempotency record together, or neither.</summary>
    Task<CommitResult> CommitAsync(CartCommit commit, CancellationToken cancellationToken);

    /// <summary>Best effort: a conflict or a missing cart is not an error.</summary>
    Task ClearAsync(string userId, string eTag, CancellationToken cancellationToken);
}

public interface ICheckoutStore
{
    /// <summary>
    /// Step one of checkout, one atomic batch: the deduct marker (which carries the order snapshot),
    /// limited-edition purchase records, the stock deductions and their audit rows.
    /// </summary>
    Task<DeductOutcome> DeductAsync(Order order, IReadOnlySet<string> limitedProductIds, CancellationToken cancellationToken);

    Task<Order?> GetSnapshotAsync(string orderId, CancellationToken cancellationToken);

    Task MarkOrderCreatedAsync(string orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> FindIncompleteAsync(DateTimeOffset olderThan, int max, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> GetPurchasedAsync(string userId, IEnumerable<string> productIds, CancellationToken cancellationToken);
}

public interface IOrderStore
{
    Task<PlacedOrder> CreateIfAbsentAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetAsync(string userId, string orderId, CancellationToken cancellationToken);

    Task<RawPage<Order>> ListAsync(
        string userId, DateTimeOffset? from, DateTimeOffset? to, int pageSize, string? token, CancellationToken cancellationToken);
}

public interface IContinuationTokenProtector
{
    string Protect(string cosmosToken, string scope);

    /// <exception cref="InvalidContinuationTokenException">Tampered, expired, or issued for another scope.</exception>
    string Unprotect(string token, string scope);
}
