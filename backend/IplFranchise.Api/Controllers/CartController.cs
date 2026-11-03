using IplFranchise.Api.Auth;
using IplFranchise.Api.Contracts;
using IplFranchise.Api.Http;
using IplFranchise.Application;
using IplFranchise.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IplFranchise.Api.Controllers;

/// <summary>
/// Every change needs an Idempotency-Key header (a GUID). Repeating a request with the same key returns the
/// first answer instead of changing the cart again.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/cart")]
public sealed class CartController(CartService cart) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CartView>(StatusCodes.Status200OK)]
    public Task<CartView> Get(CancellationToken cancellationToken) => cart.GetAsync(User.Id(), cancellationToken);

    [HttpPost("items")]
    [ProducesResponseType<CartView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<CartView> Add(AddCartItemRequest request, CancellationToken cancellationToken) =>
        cart.AddAsync(User.Id(), Request.RequireIdempotencyKey(), new AddCartItem(request.FranchiseId, request.ProductId, request.Quantity), cancellationToken);

    [HttpPut("items/{productId}")]
    [ProducesResponseType<CartView>(StatusCodes.Status200OK)]
    public Task<CartView> SetQuantity(string productId, SetQuantityRequest request, CancellationToken cancellationToken) =>
        cart.SetQuantityAsync(User.Id(), Request.RequireIdempotencyKey(), productId, request.Quantity, cancellationToken);

    [HttpDelete("items/{productId}")]
    [ProducesResponseType<CartView>(StatusCodes.Status200OK)]
    public Task<CartView> Remove(string productId, CancellationToken cancellationToken) =>
        cart.RemoveAsync(User.Id(), Request.RequireIdempotencyKey(), productId, cancellationToken);

    /// <summary>Accept the current prices. The caller sends the total it was shown, so a second price move is caught.</summary>
    [HttpPost("accept-changes")]
    [ProducesResponseType<CartView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<CartView> AcceptChanges(AcceptChangesRequest request, CancellationToken cancellationToken) =>
        cart.AcceptChangesAsync(User.Id(), Request.RequireIdempotencyKey(), request.ExpectedTotalPaise, cancellationToken);
}
