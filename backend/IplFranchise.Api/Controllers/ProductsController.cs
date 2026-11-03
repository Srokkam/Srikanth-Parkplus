using IplFranchise.Api.Contracts;
using IplFranchise.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IplFranchise.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController(CatalogueService catalogue) : ControllerBase
{
    /// <summary>
    /// Search the catalogue. Every filter is optional and they combine: franchise, type, and text in the name.
    /// Pages are ordered by name; pass the returned continuationToken to get the next page.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("search")]
    [ProducesResponseType<PageResponse<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<PageResponse<ProductResponse>> Search(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] string? franchise,
        [FromQuery] int? pageSize,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        var page = await catalogue.SearchAsync(new ProductSearchRequest(q, type, franchise, pageSize, continuationToken), cancellationToken);
        return PageResponse<ProductResponse>.From(page, ProductResponse.From);
    }

    /// <summary>One product with its live stock status. The ETag header is the version of the product record.</summary>
    [HttpGet("{franchiseId}/{productId}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ProductResponse> Get(string franchiseId, string productId, CancellationToken cancellationToken)
    {
        var details = await catalogue.GetAsync(franchiseId, productId, cancellationToken);
        Response.Headers.ETag = details.ETag;
        return ProductResponse.From(details);
    }
}
