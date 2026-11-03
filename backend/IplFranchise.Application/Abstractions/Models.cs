using IplFranchise.Domain;

namespace IplFranchise.Application;

public sealed record Versioned<T>(T Value, string ETag);

public sealed record RawPage<T>(IReadOnlyList<T> Items, string? NextToken, int? TotalCount = null);

public sealed record Page<T>(IReadOnlyList<T> Items, string? ContinuationToken, int? TotalCount);

public sealed record ProductSearch(string? Text, string? Type, string? FranchiseId, int PageSize, string? Token, bool WithTotal);

public sealed record ProductListItem(Product Product, int Available, StockStatus Status);

public sealed record ProductDetails(Product Product, int Available, StockStatus Status, string ETag);

public sealed record AuditEntry(
    string Id,
    string ProductId,
    string Field,
    long? OldValue,
    long? NewValue,
    long? Delta,
    string Reason,
    string Actor,
    DateTimeOffset TsUtc);

public sealed record AdminProductView(Product Product, int Available, string PriceETag, string StockETag);

/// <summary>A new product as an admin enters it. Validated by <see cref="ProductAdminService.CreateAsync"/>.</summary>
public sealed record NewProductRequest(
    string? FranchiseId,
    string? Id,
    string? Type,
    string? Name,
    string? Description,
    long PricePaise,
    bool LimitedEdition,
    int? MaxPerCustomer,
    int InitialStock);

/// <param name="Available">The stock level after creation (an existing stock record is kept, not overwritten).</param>
public sealed record CreatedProduct(Versioned<Product> Product, int Available);

public sealed record IdempotencyRecord(string Key, string Fingerprint, string ResponseJson);

public sealed record CartCommit(string UserId, Cart? NewCart, string? ExpectedETag, IdempotencyRecord Record);

public enum CommitResult
{
    Committed,
    ETagConflict,
    DuplicateRequest,
}

public enum DeductOutcome
{
    Deducted,
    AlreadyDeducted,
    InsufficientStock,
    LimitReached,
}

public sealed record PlacedOrder(Order Order, bool Created);
