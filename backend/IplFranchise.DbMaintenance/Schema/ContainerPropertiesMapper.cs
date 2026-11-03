using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Schema;

/// <summary>
/// Converts between the SDK-free spec and the SDK's <see cref="ContainerProperties"/>.
/// Provisioning and verification both go through here, so they can't disagree.
/// </summary>
public static class ContainerPropertiesMapper
{
    public static ContainerProperties ToProperties(ContainerSpec spec)
    {
        var properties = new ContainerProperties(spec.Name, spec.PartitionKeyPath)
        {
            DefaultTimeToLive = spec.DefaultTtlSeconds,
            IndexingPolicy = new IndexingPolicy
            {
                Automatic = true,
                IndexingMode = IndexingMode.Consistent,
            },
        };

        properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
        properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = SchemaComparer.EtagExcludedPath });
        foreach (var path in spec.ExcludedPaths)
        {
            properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = path });
        }

        foreach (var index in spec.CompositeIndexes)
        {
            properties.IndexingPolicy.CompositeIndexes.Add(new System.Collections.ObjectModel.Collection<CompositePath>(
                index.Select(p => new CompositePath
                {
                    Path = p.Path,
                    Order = p.Order == IndexOrder.Ascending ? CompositePathSortOrder.Ascending : CompositePathSortOrder.Descending,
                }).ToList()));
        }

        return properties;
    }

    public static ContainerSnapshot ToSnapshot(ContainerProperties properties) => new(
        properties.Id,
        properties.PartitionKeyPath,
        properties.DefaultTimeToLive,
        properties.IndexingPolicy.ExcludedPaths.Select(p => p.Path).ToList(),
        properties.IndexingPolicy.CompositeIndexes
            .Select(index => (IReadOnlyList<CompositeIndexPath>)index
                .Select(p => new CompositeIndexPath(p.Path, p.Order == CompositePathSortOrder.Ascending ? IndexOrder.Ascending : IndexOrder.Descending))
                .ToList())
            .ToList());
}
