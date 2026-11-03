using System.Net;
using System.Text.Json;
using IplFranchise.Application;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Infrastructure;

/// <param name="Where">Filter without the WHERE keyword; parameters go in <paramref name="Parameters"/>.</param>
/// <param name="SortField">A string property; ties are broken by id in the same direction.</param>
internal sealed record SeekQuery(
    string Alias, string Where, IReadOnlyList<(string Name, object Value)> Parameters, string SortField, bool Descending);

/// <summary>The sort key of the last row on a page: where the next page starts.</summary>
internal sealed record SeekKey(string Value, string Id);

internal static class CosmosHelpers
{
    public static PartitionKey Tenant { get; } = new("default");

    public static async Task<(T Doc, string ETag)?> TryReadAsync<T>(
        Container container, string id, PartitionKey key, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await container.ReadItemAsync<T>(id, key, cancellationToken: cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public static async Task<List<T>> ReadManyAsync<T>(
        Container container, IEnumerable<(string Id, PartitionKey Key)> items, CancellationToken cancellationToken)
    {
        var list = items.DistinctBy(i => i.Id).ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var response = await container.ReadManyItemsAsync<T>(list, cancellationToken: cancellationToken);
        Telemetry.RequestCharge.Record(response.RequestCharge);
        return response.ToList();
    }

    public static async Task<TransactionalBatchResponse> RunAsync(TransactionalBatch batch, CancellationToken cancellationToken)
    {
        var response = await batch.ExecuteAsync(cancellationToken);
        Telemetry.RequestCharge.Record(response.RequestCharge);
        return response;
    }

    /// <summary>
    /// When a batch fails, Cosmos DB reports the real status on the operation that failed and 424 on all the others.
    /// </summary>
    public static (int Index, HttpStatusCode Status) FailedOperation(TransactionalBatchResponse response)
    {
        for (var i = 0; i < response.Count; i++)
        {
            if (response[i].StatusCode != HttpStatusCode.FailedDependency)
            {
                return (i, response[i].StatusCode);
            }
        }

        return (-1, response.StatusCode);
    }

    public static Exception Unexpected(HttpStatusCode status, string what) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.RequestTimeout
            ? new DependencyUnavailableException($"The database is busy ({(int)status}) while {what}.")
            : new InvalidOperationException($"Unexpected database status {(int)status} while {what}.");

    /// <summary>
    /// Reads up to <paramref name="pageSize"/> items. A Cosmos DB page can come back short, so keep going until the page is full
    /// or the query is finished. The returned token is null only when there is nothing more.
    /// </summary>
    public static async Task<RawPage<T>> ReadPageAsync<T>(
        Container container,
        QueryDefinition query,
        PartitionKey? key,
        int pageSize,
        string? token,
        CancellationToken cancellationToken)
    {
        var items = new List<T>(pageSize);
        var continuation = token;

        do
        {
            var options = new QueryRequestOptions { MaxItemCount = pageSize - items.Count };
            if (key is { } partition)
            {
                options.PartitionKey = partition;
            }

            var response = await container.GetItemQueryIterator<T>(query, continuation, options).ReadNextAsync(cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            items.AddRange(response);
            continuation = response.ContinuationToken;
        }
        while (items.Count < pageSize && continuation is not null);

        return new RawPage<T>(items, continuation);
    }

    /// <summary>
    /// Keyset ("seek") paging over <paramref name="query"/>, ordered by its sort field and then id. Each page is one
    /// query with TOP pageSize + 1 that starts after the last row of the previous page, so the page size is enforced by
    /// the query itself, every page costs about the same however deep the caller goes, and the token is just the last
    /// row's sort key. (Cosmos DB's own continuation tokens for ORDER BY are engine state, and the emulator ignores
    /// MaxItemCount on ORDER BY queries.) The sort field and id must have a matching composite index.
    /// </summary>
    public static async Task<RawPage<T>> ReadSeekPageAsync<T>(
        Container container,
        SeekQuery query,
        PartitionKey? key,
        int pageSize,
        string? token,
        Func<T, SeekKey> keyOf,
        CancellationToken cancellationToken)
    {
        var after = ParseSeekKey(token);
        var a = query.Alias;
        var (comparison, direction) = query.Descending ? ("<", "DESC") : (">", "ASC");

        var where = query.Where;
        if (after is not null)
        {
            where += $" AND ({a}.{query.SortField} {comparison} @seekValue OR ({a}.{query.SortField} = @seekValue AND {a}.id {comparison} @seekId))";
        }

        var definition = new QueryDefinition(
                $"SELECT TOP @take * FROM {a} WHERE {where} ORDER BY {a}.{query.SortField} {direction}, {a}.id {direction}")
            .WithParameter("@take", pageSize + 1);

        foreach (var (name, value) in query.Parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        if (after is not null)
        {
            definition = definition.WithParameter("@seekValue", after.Value).WithParameter("@seekId", after.Id);
        }

        var options = new QueryRequestOptions { MaxItemCount = pageSize + 1 };
        if (key is { } partition)
        {
            options.PartitionKey = partition;
        }

        var items = new List<T>(pageSize + 1);
        using var iterator = container.GetItemQueryIterator<T>(definition, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            items.AddRange(response);
        }

        // The extra row only tells us whether another page exists; it is not returned.
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveRange(pageSize, items.Count - pageSize);
        }

        return new RawPage<T>(items, hasMore ? JsonSerializer.Serialize(keyOf(items[^1])) : null);
    }

    private static SeekKey? ParseSeekKey(string? token)
    {
        if (token is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SeekKey>(token) is { Value: not null, Id: not null } key ? key : throw new InvalidContinuationTokenException();
        }
        catch (JsonException)
        {
            // For example a token issued before seek paging, still inside its lifetime.
            throw new InvalidContinuationTokenException();
        }
    }

    public static async Task<int> CountAsync(Container container, QueryDefinition query, PartitionKey? key, CancellationToken cancellationToken)
    {
        var options = new QueryRequestOptions();
        if (key is { } partition)
        {
            options.PartitionKey = partition;
        }

        var iterator = container.GetItemQueryIterator<int>(query, requestOptions: options);
        var total = 0;
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            Telemetry.RequestCharge.Record(response.RequestCharge);
            total += response.Sum();
        }

        return total;
    }
}
