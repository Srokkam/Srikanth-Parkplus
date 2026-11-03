using System.Text.Json.Serialization;

namespace IplFranchise.Infrastructure.Documents;

// Shapes of the stored JSON. Property names are camelCase through the client's serializer settings.
// Money is integer paise. Instants are fixed-width UTC strings (see Timestamps).

internal static class Kinds
{
    public const string Tenant = "default";
}

internal sealed record ProductDoc
{
    public required string Id { get; init; }
    public string TenantId { get; init; } = Kinds.Tenant;
    public required string FranchiseId { get; init; }
    public string DocKind { get; init; } = "product";
    public int SchemaVersion { get; init; } = 1;
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required string NameLower { get; init; }
    public string Description { get; init; } = string.Empty;
    public required long PricePaise { get; init; }
    public string Currency { get; init; } = "INR";
    public bool LimitedEdition { get; init; }
    public int? MaxPerCustomer { get; init; }

    [JsonPropertyName("_etag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ETag { get; init; }
}

internal sealed record StockDoc
{
    public required string Id { get; init; }
    public string TenantId { get; init; } = Kinds.Tenant;
    public string DocKind { get; init; } = "stock";
    public int SchemaVersion { get; init; } = 1;
    public required string ProductId { get; init; }
    public required int Available { get; init; }

    [JsonPropertyName("_etag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ETag { get; init; }
}

internal sealed record StockRow(string ProductId, int Available);

/// <summary>Price changes live in the products partition (franchiseId); stock changes in the inventory partition (tenantId).</summary>
internal sealed record AuditDoc
{
    public required string Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FranchiseId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TenantId { get; init; }

    public string DocKind { get; init; } = "audit";
    public required string ProductId { get; init; }
    public required string Field { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OldValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NewValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Delta { get; init; }

    public required string Reason { get; init; }
    public required string Actor { get; init; }
    public required string TsUtc { get; init; }
}

internal sealed record CartLineDoc(string ProductId, string FranchiseId, int Quantity, long AcceptedUnitPricePaise);

internal sealed record CartDoc
{
    public const string CartId = "cart";

    public string Id { get; init; } = CartId;
    public required string UserId { get; init; }
    public string TenantId { get; init; } = Kinds.Tenant;
    public string DocKind { get; init; } = "cart";
    public required List<CartLineDoc> Lines { get; init; }
    public required string UpdatedUtc { get; init; }

    [JsonPropertyName("_etag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ETag { get; init; }
}

internal sealed record IdempotencyDoc
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string DocKind { get; init; } = "idempotency";
    public required string Fingerprint { get; init; }
    public required string ResponseJson { get; init; }

    /// <summary>Seconds. Works because the carts container has a default TTL set.</summary>
    public int Ttl { get; init; } = 86_400;
}

internal sealed record OrderLineDoc(
    string ProductId, string FranchiseId, string Name, long UnitPricePaise, int Quantity, long LineTotalPaise);

internal sealed record OrderDoc
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string TenantId { get; init; } = Kinds.Tenant;
    public string DocKind { get; init; } = "order";
    public int SchemaVersion { get; init; } = 1;
    public required string CheckoutId { get; init; }
    public string Status { get; init; } = "confirmed";
    public required string CreatedUtc { get; init; }
    public string Currency { get; init; } = "INR";
    public required List<OrderLineDoc> Lines { get; init; }
    public required long SubtotalPaise { get; init; }
    public required long ShippingPaise { get; init; }
    public required long TotalPaise { get; init; }
}

/// <summary>Written in the same batch as the stock deduction. Holds the whole order so an interrupted checkout can be finished.</summary>
internal sealed record DeductDoc
{
    public required string Id { get; init; }
    public string TenantId { get; init; } = Kinds.Tenant;
    public string DocKind { get; init; } = "deduct";
    public required string OrderId { get; init; }
    public required string UserId { get; init; }
    public required string CreatedUtc { get; init; }
    public bool OrderCreated { get; init; }
    public required OrderDoc Order { get; init; }
}

internal sealed record PurchaseDoc
{
    public required string Id { get; init; }
    public string TenantId { get; init; } = Kinds.Tenant;
    public string DocKind { get; init; } = "purchase";
    public required string UserId { get; init; }
    public required string ProductId { get; init; }
    public required string OrderId { get; init; }
}

/// <summary>Reads just the version of any document, whatever its shape.</summary>
internal sealed record ETagOnly
{
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
