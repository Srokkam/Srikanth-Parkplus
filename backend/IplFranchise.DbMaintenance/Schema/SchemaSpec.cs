using System.Text.Json;
using System.Text.Json.Serialization;

namespace IplFranchise.DbMaintenance.Schema;

/// <summary>
/// The expected database schema, loaded from Schema/schema.json (embedded in this assembly).
/// Pure data with no SDK types, so it can be unit tested without a database.
/// </summary>
public static class SchemaSpec
{
    public const string MigrationsContainer = "migrations";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static SchemaDefinition Current { get; } = Load();

    private static SchemaDefinition Load()
    {
        var assembly = typeof(SchemaSpec).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".schema.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;

        return JsonSerializer.Deserialize<SchemaDefinition>(stream, JsonOptions)
            ?? throw new InvalidDataException("Schema/schema.json is empty.");
    }
}

public sealed record SchemaDefinition(string DatabaseName, IReadOnlyList<ContainerSpec> Containers);

/// <param name="DefaultTtlSeconds">null = TTL off (documents never expire); -1 = on without a default; above 0 = default lifetime.</param>
/// <param name="ExcludedPaths">Paths left out of the index, in addition to /"_etag"/? which is always excluded.</param>
/// <param name="CompositeIndexes">One entry per multi-field ORDER BY, in the query's order and direction.</param>
/// <param name="DocKinds">The docKind values stored in the container (documentation; every query filters on docKind).</param>
public sealed record ContainerSpec(
    string Name,
    string PartitionKeyPath,
    int? DefaultTtlSeconds,
    IReadOnlyList<string> ExcludedPaths,
    IReadOnlyList<IReadOnlyList<CompositeIndexPath>> CompositeIndexes,
    IReadOnlyList<string> DocKinds,
    string Why);

public sealed record CompositeIndexPath(string Path, IndexOrder Order);

public enum IndexOrder
{
    Ascending,
    Descending,
}
