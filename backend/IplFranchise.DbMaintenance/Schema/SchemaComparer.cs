namespace IplFranchise.DbMaintenance.Schema;

/// <summary>What a container actually looks like, mapped from the SDK so the comparison stays SDK-free.</summary>
public sealed record ContainerSnapshot(
    string Name,
    string? PartitionKeyPath,
    int? DefaultTtlSeconds,
    IReadOnlyCollection<string> ExcludedPaths,
    IReadOnlyList<IReadOnlyList<CompositeIndexPath>> CompositeIndexes);

/// <summary>
/// Compares the expected schema with the containers that exist. Every difference is drift:
/// a missing or unexpected container, a different partition key, TTL, excluded path or composite index.
/// </summary>
public static class SchemaComparer
{
    /// <summary>Cosmos DB adds this to every indexing policy; it is never part of the declaration.</summary>
    public const string EtagExcludedPath = "/\"_etag\"/?";

    public static IReadOnlyList<string> Compare(
        SchemaDefinition expected,
        IReadOnlyList<ContainerSnapshot> actual,
        bool compareIndexing = true)
    {
        var drift = new List<string>();

        foreach (var spec in expected.Containers)
        {
            var container = actual.FirstOrDefault(c => c.Name == spec.Name);
            if (container is null)
            {
                drift.Add($"Container '{spec.Name}' is missing.");
                continue;
            }

            if (container.PartitionKeyPath != spec.PartitionKeyPath)
            {
                drift.Add($"Container '{spec.Name}': partition key is {container.PartitionKeyPath ?? "(none)"}, expected {spec.PartitionKeyPath}.");
            }

            if (container.DefaultTtlSeconds != spec.DefaultTtlSeconds)
            {
                drift.Add($"Container '{spec.Name}': default TTL is {FormatTtl(container.DefaultTtlSeconds)}, expected {FormatTtl(spec.DefaultTtlSeconds)}.");
            }

            if (!compareIndexing)
            {
                continue;
            }

            var actualExcluded = container.ExcludedPaths.Where(p => p != EtagExcludedPath).Order(StringComparer.Ordinal);
            if (!actualExcluded.SequenceEqual(spec.ExcludedPaths.Order(StringComparer.Ordinal)))
            {
                drift.Add($"Container '{spec.Name}': excluded paths are [{string.Join(", ", actualExcluded)}], expected [{string.Join(", ", spec.ExcludedPaths)}].");
            }

            var actualComposites = container.CompositeIndexes.Select(Describe).Order(StringComparer.Ordinal).ToList();
            var expectedComposites = spec.CompositeIndexes.Select(Describe).Order(StringComparer.Ordinal).ToList();
            if (!actualComposites.SequenceEqual(expectedComposites))
            {
                drift.Add($"Container '{spec.Name}': composite indexes are [{string.Join("; ", actualComposites)}], expected [{string.Join("; ", expectedComposites)}].");
            }
        }

        foreach (var extra in actual.Where(c => expected.Containers.All(spec => spec.Name != c.Name)))
        {
            drift.Add($"Container '{extra.Name}' exists but is not in Schema/schema.json. Remove it, or declare it deliberately.");
        }

        return drift;
    }

    private static string FormatTtl(int? seconds) => seconds switch
    {
        null => "off",
        -1 => "on (-1, no default expiry)",
        _ => $"{seconds} s",
    };

    private static string Describe(IReadOnlyList<CompositeIndexPath> index) =>
        string.Join(", ", index.Select(p => $"{p.Path} {(p.Order == IndexOrder.Ascending ? "asc" : "desc")}"));
}
