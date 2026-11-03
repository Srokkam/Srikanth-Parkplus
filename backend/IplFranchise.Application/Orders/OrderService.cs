using IplFranchise.Domain;

namespace IplFranchise.Application;

public sealed record OrderListRequest(DateTimeOffset? From, DateTimeOffset? To, int? PageSize, string? ContinuationToken);

public sealed class OrderService(IOrderStore orders, IContinuationTokenProtector tokens)
{
    public async Task<Page<Order>> ListAsync(string userId, OrderListRequest request, CancellationToken cancellationToken)
    {
        if (request.From is { } from && request.To is { } to && from >= to)
        {
            throw new ValidationFailedException("from", "'from' must be earlier than 'to'.");
        }

        var pageSize = Paging.ValidatePageSize(request.PageSize);
        var scope = Paging.Scope("orders", userId, request.From?.UtcTicks, request.To?.UtcTicks, pageSize);
        var rawToken = Paging.Open(tokens, request.ContinuationToken, scope);

        var page = await orders.ListAsync(userId, request.From, request.To, pageSize, rawToken, cancellationToken);
        return new Page<Order>(page.Items, Paging.Seal(tokens, page.NextToken, scope), null);
    }

    public async Task<Order> GetAsync(string userId, string orderId, CancellationToken cancellationToken) =>
        await orders.GetAsync(userId, orderId, cancellationToken)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
}
