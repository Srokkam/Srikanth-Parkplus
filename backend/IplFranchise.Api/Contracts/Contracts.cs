using System.ComponentModel.DataAnnotations;
using IplFranchise.Application;
using IplFranchise.Domain;

namespace IplFranchise.Api.Contracts;

public sealed record PageResponse<T>(IReadOnlyList<T> Items, string? ContinuationToken, int? TotalCount)
{
    public static PageResponse<TOut> From<TIn, TOut>(Page<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.ContinuationToken, page.TotalCount);
}

public sealed record ProductResponse(
    string Id,
    string FranchiseId,
    string Name,
    string Type,
    string Description,
    long PricePaise,
    bool LimitedEdition,
    int? MaxPerCustomer,
    int Available,
    StockStatus StockStatus)
{
    public static ProductResponse From(ProductListItem item) => From(item.Product, item.Available, item.Status);

    public static ProductResponse From(ProductDetails details) => From(details.Product, details.Available, details.Status);

    public static ProductResponse From(Product p, int available, StockStatus status) =>
        new(p.Id, p.FranchiseId, p.Name, p.Type, p.Description, p.PricePaise, p.LimitedEdition, p.MaxPerCustomer, available, status);
}

public sealed record FranchiseResponse(string Code);

public sealed record OrderLineResponse(string ProductId, string FranchiseId, string Name, long UnitPricePaise, int Quantity, long LineTotalPaise);

/// <summary>
/// An order as v1 clients see it. Fields are only ever added within v1, never renamed, removed or changed in meaning;
/// a breaking change would be a new /api/v2 route served side by side.
/// </summary>
/// <param name="ItemCount">
/// Added to v1 after release (backwards compatible): the number of items across all lines. Optional for clients;
/// older clients ignore unknown fields and can still sum the lines themselves.
/// </param>
public sealed record OrderResponse(
    string Id,
    string UserId,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<OrderLineResponse> Lines,
    long ShippingPaise,
    long SubtotalPaise,
    long TotalPaise,
    int ItemCount)
{
    public static OrderResponse From(Order order) =>
        new(
            order.Id,
            order.UserId,
            order.CreatedUtc,
            order.Lines.Select(l => new OrderLineResponse(l.ProductId, l.FranchiseId, l.Name, l.UnitPricePaise, l.Quantity, l.LineTotalPaise)).ToList(),
            order.ShippingPaise,
            order.SubtotalPaise,
            order.TotalPaise,
            order.Lines.Sum(l => l.Quantity));
}

/// <param name="Id">Product slug starting with the franchise code, e.g. "csk-cap". Never reused.</param>
/// <param name="MaxPerCustomer">Required for a limited edition (1 to 10); must be empty otherwise.</param>
public sealed record CreateProductRequest(
    string? FranchiseId,
    string? Id,
    string? Type,
    string? Name,
    string? Description,
    long PricePaise,
    bool LimitedEdition,
    int? MaxPerCustomer,
    int InitialStock);

public sealed record AddCartItemRequest(
    [Required, StringLength(40)] string FranchiseId,
    [Required, StringLength(80)] string ProductId,
    int Quantity);

public sealed record SetQuantityRequest(int Quantity);

public sealed record AcceptChangesRequest(long ExpectedTotalPaise);

public sealed record PlaceOrderRequest(long AcceptedTotalPaise);

public sealed record AdminProductResponse(
    string Id,
    string FranchiseId,
    string Name,
    long PricePaise,
    int Available,
    string PriceETag,
    string StockETag);

public sealed record ChangePriceRequest(long PricePaise, [StringLength(200)] string? Reason);

public sealed record SetStockRequest(int Available, [StringLength(200)] string? Reason);

public sealed record PriceResponse(string ProductId, long PricePaise);

public sealed record StockResponse(string ProductId, int Available, StockStatus StockStatus);

public sealed record AuditEntryResponse(
    string Id,
    string Field,
    long? OldValue,
    long? NewValue,
    long? Delta,
    string Reason,
    string Actor,
    DateTimeOffset TsUtc)
{
    public static AuditEntryResponse From(AuditEntry e) => new(e.Id, e.Field, e.OldValue, e.NewValue, e.Delta, e.Reason, e.Actor, e.TsUtc);
}

public sealed record DevTokenRequest([Required] string UserId, bool Admin);

public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresUtc);
