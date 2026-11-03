using IplFranchise.Api.Auth;
using IplFranchise.Api.Contracts;
using IplFranchise.Api.Http;
using IplFranchise.Application;
using IplFranchise.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IplFranchise.Api.Controllers;

/// <summary>
/// Products are created here. Price and stock edits send the version they last read in If-Match. Without it the call is
/// refused (428); if someone else changed the record meanwhile it is refused again (412) and nothing is overwritten.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.Admin)]
[Route("api/v1/admin/products")]
public sealed class AdminProductsController(ProductAdminService admin) : ControllerBase
{
    /// <summary>
    /// Adds a product with its opening stock. The id is chosen by the caller, so repeating the call can't create a
    /// second product: it answers 409 product-exists instead.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var created = await admin.CreateAsync(
            new NewProductRequest(
                request.FranchiseId,
                request.Id,
                request.Type,
                request.Name,
                request.Description,
                request.PricePaise,
                request.LimitedEdition,
                request.MaxPerCustomer,
                request.InitialStock),
            User.Id(),
            cancellationToken);

        Response.Headers.ETag = created.ETag;
        return Created($"/api/v1/products/{created.Product.FranchiseId}/{created.Product.Id}", ProductResponse.From(created));
    }

    [HttpGet("{franchiseId}/{productId}")]
    [ProducesResponseType<AdminProductResponse>(StatusCodes.Status200OK)]
    public async Task<AdminProductResponse> Get(string franchiseId, string productId, CancellationToken cancellationToken)
    {
        var view = await admin.GetAsync(franchiseId, productId, cancellationToken);
        return new AdminProductResponse(
            view.Product.Id, view.Product.FranchiseId, view.Product.Name, view.Product.PricePaise, view.Available, view.PriceETag, view.StockETag);
    }

    [HttpPut("{franchiseId}/{productId}/price")]
    [ProducesResponseType<PriceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<PriceResponse> ChangePrice(
        string franchiseId, string productId, ChangePriceRequest request, CancellationToken cancellationToken)
    {
        var updated = await admin.ChangePriceAsync(
            franchiseId, productId, request.PricePaise, Request.IfMatch(), request.Reason, User.Id(), cancellationToken);

        Response.Headers.ETag = updated.ETag;
        return new PriceResponse(updated.Value.Id, updated.Value.PricePaise);
    }

    [HttpPut("{franchiseId}/{productId}/stock")]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<StockResponse> SetStock(
        string franchiseId, string productId, SetStockRequest request, CancellationToken cancellationToken)
    {
        var updated = await admin.SetStockAsync(
            franchiseId, productId, request.Available, Request.IfMatch(), request.Reason, User.Id(), cancellationToken);

        Response.Headers.ETag = updated.ETag;
        return new StockResponse(productId, updated.Value, StockPolicy.StatusFor(updated.Value));
    }

    /// <summary>Change history, newest first. field=price or field=stock.</summary>
    [HttpGet("{franchiseId}/{productId}/audit")]
    [ProducesResponseType<PageResponse<AuditEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<PageResponse<AuditEntryResponse>> Audit(
        string franchiseId,
        string productId,
        [FromQuery] string? field,
        [FromQuery] int? pageSize,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        var page = await admin.ListAuditAsync(franchiseId, productId, field, pageSize, continuationToken, cancellationToken);
        return PageResponse<AuditEntryResponse>.From(page, AuditEntryResponse.From);
    }
}
