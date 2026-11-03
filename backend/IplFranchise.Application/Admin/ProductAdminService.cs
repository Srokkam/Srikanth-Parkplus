using IplFranchise.Domain;

namespace IplFranchise.Application;

public sealed class ProductAdminService(
    IProductStore products,
    IStockStore stock,
    ICatalogueAdminStore admin,
    IContinuationTokenProtector tokens,
    TimeProvider clock)
{
    public const long MaxPricePaise = 100_000_000;
    public const int MaxStock = 1_000_000;
    private const int MaxReasonLength = 200;

    /// <summary>Adds a product to the catalogue with its opening stock. Every invalid field is reported at once.</summary>
    public async Task<ProductDetails> CreateAsync(NewProductRequest request, string actor, CancellationToken cancellationToken)
    {
        var franchiseId = request.FranchiseId?.Trim() ?? string.Empty;
        var id = request.Id?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();

        if (!FranchiseCodes.IsValid(franchiseId))
        {
            errors["franchiseId"] = [$"Franchise must be a code of {FranchiseCodes.MinLength} to {FranchiseCodes.MaxLength} lower-case letters, e.g. 'csk'."];
        }
        else if (!ProductRules.IsValidId(id, franchiseId))
        {
            errors["id"] = [$"Id must be lower-case words joined by dashes, start with '{franchiseId}-' and be at most {ProductRules.MaxIdLength} characters, e.g. '{franchiseId}-cap'."];
        }

        if (!ProductTypes.IsValid(request.Type))
        {
            errors["type"] = [$"Type must be one of: {string.Join(", ", ProductTypes.All)}."];
        }

        if (name.Length is 0 or > ProductRules.MaxNameLength)
        {
            errors["name"] = [$"Name must be 1 to {ProductRules.MaxNameLength} characters."];
        }

        if (description.Length > ProductRules.MaxDescriptionLength)
        {
            errors["description"] = [$"Description can be at most {ProductRules.MaxDescriptionLength} characters."];
        }

        if (request.PricePaise is < 1 or > MaxPricePaise)
        {
            errors["pricePaise"] = [$"Price must be between 1 and {MaxPricePaise} paise."];
        }

        // maxPerCustomer is what makes a limited edition limited, so the two go together.
        if (request.LimitedEdition && request.MaxPerCustomer is not (>= 1 and <= Limits.MaxQuantityPerLine))
        {
            errors["maxPerCustomer"] = [$"A limited edition needs a maximum per customer between 1 and {Limits.MaxQuantityPerLine}."];
        }
        else if (!request.LimitedEdition && request.MaxPerCustomer is not null)
        {
            errors["maxPerCustomer"] = ["Only limited editions have a maximum per customer."];
        }

        if (request.InitialStock is < 0 or > MaxStock)
        {
            errors["initialStock"] = [$"Initial stock must be between 0 and {MaxStock}."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var product = new Product(
            id, franchiseId, name, request.Type!, description, request.PricePaise, request.LimitedEdition, request.MaxPerCustomer);

        var created = await admin.CreateProductAsync(product, request.InitialStock, actor, clock.GetUtcNow(), cancellationToken);
        return new ProductDetails(created.Product.Value, created.Available, StockPolicy.StatusFor(created.Available), created.Product.ETag);
    }

    public async Task<AdminProductView> GetAsync(string franchiseId, string productId, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(franchiseId, productId, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");
        var level = await stock.GetAsync(productId, cancellationToken)
            ?? throw new NotFoundException($"Stock for product '{productId}' was not found.");

        return new AdminProductView(product.Value, level.Value, product.ETag, level.ETag);
    }

    public Task<Versioned<Product>> ChangePriceAsync(
        string franchiseId, string productId, long pricePaise, string? eTag, string? reason, string actor, CancellationToken cancellationToken)
    {
        if (pricePaise is < 1 or > MaxPricePaise)
        {
            throw new ValidationFailedException("pricePaise", $"Price must be between 1 and {MaxPricePaise} paise.");
        }

        return admin.ChangePriceAsync(
            franchiseId, productId, pricePaise, RequireETag(eTag), RequireReason(reason), actor, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<Versioned<int>> SetStockAsync(
        string franchiseId, string productId, int available, string? eTag, string? reason, string actor, CancellationToken cancellationToken)
    {
        if (available is < 0 or > MaxStock)
        {
            throw new ValidationFailedException("available", $"Stock must be between 0 and {MaxStock}.");
        }

        if (await products.GetAsync(franchiseId, productId, cancellationToken) is null)
        {
            throw new NotFoundException($"Product '{productId}' was not found.");
        }

        return await admin.SetStockAsync(
            productId, available, RequireETag(eTag), RequireReason(reason), actor, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<Page<AuditEntry>> ListAuditAsync(
        string franchiseId, string productId, string? field, int? pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        if (field is not ("price" or "stock"))
        {
            throw new ValidationFailedException("field", "Field must be 'price' or 'stock'.");
        }

        var size = Paging.ValidatePageSize(pageSize);
        var scope = Paging.Scope("audit", franchiseId, productId, field, size);
        var rawToken = Paging.Open(tokens, continuationToken, scope);

        var page = await admin.ListAuditAsync(field, franchiseId, productId, size, rawToken, cancellationToken);
        return new Page<AuditEntry>(page.Items, Paging.Seal(tokens, page.NextToken, scope), null);
    }

    private static string RequireETag(string? eTag) =>
        string.IsNullOrWhiteSpace(eTag) ? throw new PreconditionRequiredException() : eTag;

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxReasonLength)
        {
            throw new ValidationFailedException("reason", $"A reason of 1 to {MaxReasonLength} characters is required.");
        }

        return trimmed;
    }
}
