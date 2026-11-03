using IplFranchise.Api.Auth;
using IplFranchise.Api.Contracts;
using IplFranchise.Api.Http;
using IplFranchise.Application;
using IplFranchise.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IplFranchise.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController(CheckoutService checkout, OrderService orders) : ControllerBase
{
    /// <summary>
    /// Places the order for the current cart. The client chooses the order id (a GUID), so a retry after a timeout
    /// can never create a second order: 201 the first time, 200 with the same order on every repeat.
    /// </summary>
    [HttpPut("{orderId:guid}")]
    [EnableRateLimiting("checkout")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Place(Guid orderId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var placed = await checkout.PlaceOrderAsync(User.Id(), orderId.ToString("N"), request.AcceptedTotalPaise, cancellationToken);
        var body = OrderResponse.From(placed.Order);

        if (!placed.Created)
        {
            return Ok(body);
        }

        return Created($"/api/v1/orders/{orderId:N}", body);
    }

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<OrderResponse> Get(Guid orderId, CancellationToken cancellationToken) =>
        OrderResponse.From(await orders.GetAsync(User.Id(), orderId.ToString("N"), cancellationToken));

    /// <summary>Your orders, newest first. 'from' is inclusive and 'to' exclusive.</summary>
    [HttpGet]
    [ProducesResponseType<PageResponse<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<PageResponse<OrderResponse>> List(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? pageSize,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        var page = await orders.ListAsync(User.Id(), new OrderListRequest(from, to, pageSize, continuationToken), cancellationToken);
        return PageResponse<OrderResponse>.From(page, OrderResponse.From);
    }
}
