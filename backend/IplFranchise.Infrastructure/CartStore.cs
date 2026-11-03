using System.Net;
using IplFranchise.Application;
using IplFranchise.Domain;
using IplFranchise.Infrastructure.Documents;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

internal sealed class CartStore(CosmosContainers containers, TimeProvider clock) : ICartStore
{
    public async Task<Versioned<Cart>?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var found = await CosmosHelpers.TryReadAsync<CartDoc>(containers.Carts, CartDoc.CartId, new PartitionKey(userId), cancellationToken);
        return found is null ? null : new Versioned<Cart>(found.Value.Doc.ToDomain(), found.Value.ETag);
    }

    public async Task<IdempotencyRecord?> GetIdempotencyAsync(string userId, string key, CancellationToken cancellationToken)
    {
        var found = await CosmosHelpers.TryReadAsync<IdempotencyDoc>(containers.Carts, RecordId(key), new PartitionKey(userId), cancellationToken);
        return found is null ? null : new IdempotencyRecord(key, found.Value.Doc.Fingerprint, found.Value.Doc.ResponseJson);
    }

    public async Task<CommitResult> CommitAsync(CartCommit commit, CancellationToken cancellationToken)
    {
        var batch = containers.Carts.CreateTransactionalBatch(new PartitionKey(commit.UserId));

        // The idempotency record goes first: a duplicate key then fails on operation 0, a stale cart on operation 1.
        batch.CreateItem(new IdempotencyDoc
        {
            Id = RecordId(commit.Record.Key),
            UserId = commit.UserId,
            Fingerprint = commit.Record.Fingerprint,
            ResponseJson = commit.Record.ResponseJson,
        });

        var cartOptions = commit.ExpectedETag is null ? null : new TransactionalBatchItemRequestOptions { IfMatchEtag = commit.ExpectedETag };
        switch (commit.NewCart, commit.ExpectedETag)
        {
            case (null, null):
                break;
            case (null, _):
                batch.DeleteItem(CartDoc.CartId, cartOptions);
                break;
            case ({ } cart, null):
                batch.CreateItem(cart.ToDocument(clock.GetUtcNow()));
                break;
            case ({ } cart, _):
                batch.ReplaceItem(CartDoc.CartId, cart.ToDocument(clock.GetUtcNow()), cartOptions);
                break;
        }

        var response = await CosmosHelpers.RunAsync(batch, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return CommitResult.Committed;
        }

        var failed = CosmosHelpers.FailedOperation(response);
        return (failed.Index, failed.Status) switch
        {
            (0, HttpStatusCode.Conflict) => CommitResult.DuplicateRequest,
            (1, HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound) => CommitResult.ETagConflict,
            _ => throw CosmosHelpers.Unexpected(failed.Status, "saving the cart"),
        };
    }

    public async Task ClearAsync(string userId, string eTag, CancellationToken cancellationToken)
    {
        try
        {
            await containers.Carts.DeleteItemAsync<CartDoc>(
                CartDoc.CartId, new PartitionKey(userId), new ItemRequestOptions { IfMatchEtag = eTag }, cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
        }
    }

    private static string RecordId(string key) => $"req:{key}";
}
