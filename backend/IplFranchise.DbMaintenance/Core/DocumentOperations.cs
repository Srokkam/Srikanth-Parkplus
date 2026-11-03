using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using IplFranchise.DbMaintenance.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace IplFranchise.DbMaintenance.Core;

/// <summary>
/// The document operations available to data migrations. Every helper is idempotent: it only touches documents
/// that still need the change (checked again on the server with a conditional filter or ETag), pages with
/// continuation tokens, and caps its own concurrency. There are deliberately no container operations here:
/// containers belong to Schema/schema.json and Infrastructure/main.bicep.
/// </summary>
public sealed class DocumentOperations(Database database, DbMaintenanceOptions options, ILogger<DocumentOperations> logger)
{
    private readonly Dictionary<string, string> partitionKeyPaths = [];

    /// <summary>The underlying database, for anything the helpers don't cover. Keep it idempotent.</summary>
    public Database Database => database;

    /// <summary>
    /// Sets a top-level property on every document of <paramref name="docKind"/> that doesn't have it.
    /// Uses a patch with a "NOT IS_DEFINED" filter, so a document that gained the property meanwhile is left alone.
    /// </summary>
    /// <returns>The number of documents changed.</returns>
    public Task<int> SetIfMissingAsync<T>(
        string containerName,
        string docKind,
        string propertyName,
        T value,
        CancellationToken cancellationToken = default) =>
        PatchMatchingAsync(
            containerName,
            docKind,
            $"NOT IS_DEFINED(c[{Literal(propertyName)}])",
            [PatchOperation.Set("/" + propertyName, value)],
            cancellationToken);

    /// <summary>Renames a top-level property on every document of <paramref name="docKind"/> that still has the old name.</summary>
    public Task<int> RenamePropertyAsync(
        string containerName,
        string docKind,
        string fromName,
        string toName,
        CancellationToken cancellationToken = default) =>
        PatchMatchingAsync(
            containerName,
            docKind,
            $"IS_DEFINED(c[{Literal(fromName)}]) AND NOT IS_DEFINED(c[{Literal(toName)}])",
            [PatchOperation.Move("/" + fromName, "/" + toName)],
            cancellationToken);

    /// <summary>Removes a top-level property from every document of <paramref name="docKind"/> that has it.</summary>
    public Task<int> RemovePropertyAsync(
        string containerName,
        string docKind,
        string propertyName,
        CancellationToken cancellationToken = default) =>
        PatchMatchingAsync(
            containerName,
            docKind,
            $"IS_DEFINED(c[{Literal(propertyName)}])",
            [PatchOperation.Remove("/" + propertyName)],
            cancellationToken);

    /// <summary>
    /// Patches one document if it exists and matches <paramref name="filterPredicate"/>
    /// (e.g. "FROM c WHERE c.docKind = 'product' AND c.limitedEdition = false").
    /// </summary>
    /// <returns>True if the document was changed; false if it doesn't exist or no longer matches.</returns>
    public async Task<bool> PatchItemIfAsync(
        string containerName,
        string id,
        PartitionKey partitionKey,
        string filterPredicate,
        IReadOnlyList<PatchOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var changed = await TryPatchAsync(database.GetContainer(containerName), id, partitionKey, operations, filterPredicate, cancellationToken);
        logger.LogInformation("{Container}/{Id}: {Result}", containerName, id, changed ? "patched" : "not found or condition not met, unchanged");
        return changed;
    }

    /// <summary>
    /// Runs <paramref name="transform"/> on each document returned by <paramref name="query"/> and saves the ones it
    /// changed (it returns true), conditional on the document's ETag. Use for changes a patch can't express, such as
    /// recomputing a value from other fields. The query must filter on docKind. Runs one document at a time.
    /// </summary>
    /// <returns>The number of documents changed.</returns>
    public async Task<int> TransformItemsAsync(
        string containerName,
        QueryDefinition query,
        Func<JsonObject, bool> transform,
        CancellationToken cancellationToken = default)
    {
        var container = database.GetContainer(containerName);
        var partitionKeyPath = await GetPartitionKeyPathAsync(container, cancellationToken);
        var changed = 0;

        await foreach (var page in QueryPagesAsync<JsonObject>(container, query, cancellationToken))
        {
            foreach (var item in page)
            {
                var id = item["id"]?.GetValue<string>() ?? throw new InvalidDataException("Document has no id.");
                var etag = item["_etag"]?.GetValue<string>();
                var partitionKey = ReadPartitionKey(item, partitionKeyPath);

                if (!transform(item))
                {
                    continue;
                }

                if (ReadPartitionKey(item, partitionKeyPath).ToString() != partitionKey.ToString())
                {
                    throw new InvalidOperationException(
                        $"The transform changed the partition key of '{id}' in '{containerName}'. " +
                        "A partition key value never changes in place: copy into a new container instead.");
                }

                // A 412 here means someone changed the document meanwhile: fail, and the rerun sees the new version.
                await container.ReplaceItemAsync(
                    item, id, partitionKey, new ItemRequestOptions { IfMatchEtag = etag, EnableContentResponseOnWrite = false }, cancellationToken);
                changed++;
            }
        }

        logger.LogInformation("{Container}: transformed {Changed} document(s)", containerName, changed);
        return changed;
    }

    /// <summary>Creates a document unless one with the same id already exists in that partition.</summary>
    public Task<bool> CreateIfAbsentAsync<T>(
        string containerName,
        T item,
        PartitionKey partitionKey,
        CancellationToken cancellationToken = default) =>
        database.GetContainer(containerName).CreateIfAbsentAsync(item, partitionKey, cancellationToken);

    private async Task<int> PatchMatchingAsync(
        string containerName,
        string docKind,
        string condition,
        IReadOnlyList<PatchOperation> operations,
        CancellationToken cancellationToken)
    {
        var container = database.GetContainer(containerName);
        var partitionKeyPath = await GetPartitionKeyPathAsync(container, cancellationToken);

        // Read only id and partition key; the patch itself re-checks the condition on the server.
        var query = new QueryDefinition(
                $"SELECT c.id, {Accessor(partitionKeyPath)} AS pk FROM c WHERE c.docKind = @docKind AND {condition}")
            .WithParameter("@docKind", docKind);
        var filterPredicate = $"FROM c WHERE c.docKind = {Literal(docKind)} AND {condition}";

        var changed = 0;
        var unchanged = 0;

        await foreach (var page in QueryPagesAsync<JsonObject>(container, query, cancellationToken))
        {
            await Parallel.ForEachAsync(
                page,
                new ParallelOptions { MaxDegreeOfParallelism = options.MaxConcurrency, CancellationToken = cancellationToken },
                async (item, token) =>
                {
                    var id = item["id"]!.GetValue<string>();
                    if (await TryPatchAsync(container, id, ReadPartitionKey(item, "/pk"), operations, filterPredicate, token))
                    {
                        Interlocked.Increment(ref changed);
                    }
                    else
                    {
                        Interlocked.Increment(ref unchanged);
                    }
                });
        }

        logger.LogInformation(
            "{Container} ({DocKind}): changed {Changed} document(s), {Unchanged} already up to date", containerName, docKind, changed, unchanged);
        return changed;
    }

    private static async Task<bool> TryPatchAsync(
        Container container,
        string id,
        PartitionKey partitionKey,
        IReadOnlyList<PatchOperation> operations,
        string filterPredicate,
        CancellationToken cancellationToken)
    {
        using var response = await container.PatchItemStreamAsync(
            id,
            partitionKey,
            operations,
            new PatchItemRequestOptions { FilterPredicate = filterPredicate, EnableContentResponseOnWrite = false },
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async IAsyncEnumerable<FeedResponse<T>> QueryPagesAsync<T>(
        Container container,
        QueryDefinition query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: new QueryRequestOptions { MaxItemCount = options.PageSize });
        while (iterator.HasMoreResults)
        {
            yield return await iterator.ReadNextAsync(cancellationToken);
        }
    }

    private async Task<string> GetPartitionKeyPathAsync(Container container, CancellationToken cancellationToken)
    {
        if (!partitionKeyPaths.TryGetValue(container.Id, out var path))
        {
            ContainerProperties properties = await container.ReadContainerAsync(cancellationToken: cancellationToken);
            path = properties.PartitionKeyPath
                ?? throw new NotSupportedException($"Container '{container.Id}' uses hierarchical partition keys, which these helpers don't support.");
            partitionKeyPaths[container.Id] = path;
        }

        return path;
    }

    /// <summary>A Cosmos SQL string literal for a developer-supplied name. Never pass user input here.</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);

    /// <summary>"/a/b" becomes c["a"]["b"].</summary>
    private static string Accessor(string path) =>
        "c" + string.Concat(path.Trim('/').Split('/').Select(segment => $"[{Literal(segment)}]"));

    private static PartitionKey ReadPartitionKey(JsonObject item, string path)
    {
        JsonObject? current = item;
        JsonNode? value = null;

        foreach (var segment in path.Trim('/').Split('/'))
        {
            if (current is null || !current.TryGetPropertyValue(segment, out value))
            {
                return PartitionKey.None;
            }

            current = value as JsonObject;
        }

        return value switch
        {
            null => PartitionKey.Null,
            JsonValue v when v.TryGetValue(out string? text) => new PartitionKey(text),
            JsonValue v when v.TryGetValue(out double number) => new PartitionKey(number),
            JsonValue v when v.TryGetValue(out bool flag) => new PartitionKey(flag),
            _ => throw new NotSupportedException($"Unsupported partition key value at '{path}'."),
        };
    }
}
