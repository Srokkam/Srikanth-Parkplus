using IplFranchise.Domain;

namespace IplFranchise.Application;

public sealed record ProductSearchRequest(string? Text, string? Type, string? FranchiseId, int? PageSize, string? ContinuationToken);

public sealed class CatalogueService(IProductStore products, IStockStore stock, IContinuationTokenProtector tokens)
{
    private const int MaxSearchTextLength = 50;

    public async Task<Page<ProductListItem>> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
        {
            text = null;
        }

        var errors = new Dictionary<string, string[]>();
        if (text is { Length: > MaxSearchTextLength })
        {
            errors["q"] = [$"Search text can be at most {MaxSearchTextLength} characters."];
        }

        if (request.Type is not null && !ProductTypes.IsValid(request.Type))
        {
            errors["type"] = [$"Type must be one of: {string.Join(", ", ProductTypes.All)}."];
        }

        // Franchises come from the catalogue, so an unknown but well-formed code simply finds nothing.
        if (request.FranchiseId is not null && !FranchiseCodes.IsValid(request.FranchiseId))
        {
            errors["franchise"] = [$"Franchise must be a code of {FranchiseCodes.MinLength} to {FranchiseCodes.MaxLength} lower-case letters."];
        }

        var pageSize = 0;
        try
        {
            pageSize = Paging.ValidatePageSize(request.PageSize);
        }
        catch (ValidationFailedException ex)
        {
            foreach (var (field, messages) in ex.Errors)
            {
                errors[field] = messages;
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var scope = Paging.Scope("products", text, request.Type, request.FranchiseId, pageSize);
        var rawToken = Paging.Open(tokens, request.ContinuationToken, scope);

        var page = await products.SearchAsync(
            new ProductSearch(text, request.Type, request.FranchiseId, pageSize, rawToken, WithTotal: rawToken is null),
            cancellationToken);

        var available = await stock.GetManyAsync(page.Items.Select(p => p.Id), cancellationToken);
        var items = page.Items
            .Select(p =>
            {
                var count = available.GetValueOrDefault(p.Id);
                return new ProductListItem(p, count, StockPolicy.StatusFor(count));
            })
            .ToList();

        return new Page<ProductListItem>(items, Paging.Seal(tokens, page.NextToken, scope), page.TotalCount);
    }

    /// <summary>The franchises that have at least one product, in code order.</summary>
    public async Task<IReadOnlyList<string>> ListFranchisesAsync(CancellationToken cancellationToken) =>
        (await products.ListFranchiseIdsAsync(cancellationToken)).Order(StringComparer.Ordinal).ToList();

    public async Task<ProductDetails> GetAsync(string franchiseId, string productId, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(franchiseId, productId, cancellationToken)
            ?? throw new NotFoundException($"Product '{productId}' was not found.");

        var available = (await stock.GetAsync(productId, cancellationToken))?.Value ?? 0;
        return new ProductDetails(product.Value, available, StockPolicy.StatusFor(available), product.ETag);
    }
}
