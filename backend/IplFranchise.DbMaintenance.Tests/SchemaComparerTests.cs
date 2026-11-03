using IplFranchise.DbMaintenance.Schema;

namespace IplFranchise.DbMaintenance.Tests;

public class SchemaComparerTests
{
    private static readonly SchemaDefinition Schema = SchemaSpec.Current;

    /// <summary>What provision creates, read back the way verify-schema reads it.</summary>
    private static List<ContainerSnapshot> Provisioned() =>
        Schema.Containers
            .Select(spec => ContainerPropertiesMapper.ToSnapshot(ContainerPropertiesMapper.ToProperties(spec)))
            .ToList();

    [Fact]
    public void What_provision_creates_is_exactly_what_verify_expects() =>
        Assert.Empty(SchemaComparer.Compare(Schema, Provisioned()));

    [Fact]
    public void Provisioned_containers_always_exclude_etag_from_the_index() =>
        Assert.All(Provisioned(), c => Assert.Contains(SchemaComparer.EtagExcludedPath, c.ExcludedPaths));

    [Fact]
    public void Missing_container_is_drift()
    {
        var actual = Provisioned().Where(c => c.Name != "orders").ToList();
        Assert.Equal(["Container 'orders' is missing."], SchemaComparer.Compare(Schema, actual));
    }

    [Fact]
    public void Different_partition_key_is_drift()
    {
        var actual = Replace(Provisioned(), "carts", c => c with { PartitionKeyPath = "/id" });
        Assert.Contains("partition key is /id, expected /userId", Assert.Single(SchemaComparer.Compare(Schema, actual)));
    }

    [Fact]
    public void Turning_off_ttl_on_carts_is_drift()
    {
        var actual = Replace(Provisioned(), "carts", c => c with { DefaultTtlSeconds = null });
        Assert.Contains("default TTL is off, expected 2592000 s", Assert.Single(SchemaComparer.Compare(Schema, actual)));
    }

    [Fact]
    public void Enabling_ttl_on_orders_is_drift()
    {
        var actual = Replace(Provisioned(), "orders", c => c with { DefaultTtlSeconds = -1 });
        Assert.Contains("Container 'orders': default TTL", Assert.Single(SchemaComparer.Compare(Schema, actual)));
    }

    [Fact]
    public void Missing_composite_index_is_drift()
    {
        var actual = Replace(Provisioned(), "products", c => c with { CompositeIndexes = [] });
        Assert.Contains("Container 'products': composite indexes", Assert.Single(SchemaComparer.Compare(Schema, actual)));
    }

    [Fact]
    public void Composite_index_direction_matters()
    {
        var actual = Replace(Provisioned(), "orders", c => c with
        {
            CompositeIndexes = [[new CompositeIndexPath("/createdUtc", IndexOrder.Ascending), new CompositeIndexPath("/id", IndexOrder.Ascending)]],
        });
        Assert.Single(SchemaComparer.Compare(Schema, actual));
    }

    [Fact]
    public void Indexed_description_is_drift()
    {
        var actual = Replace(Provisioned(), "products", c => c with { ExcludedPaths = [SchemaComparer.EtagExcludedPath] });
        Assert.Contains("excluded paths", Assert.Single(SchemaComparer.Compare(Schema, actual)));
    }

    [Fact]
    public void Indexing_differences_are_ignored_when_index_comparison_is_off()
    {
        var actual = Replace(Provisioned(), "products", c => c with { ExcludedPaths = [], CompositeIndexes = [] });
        Assert.Empty(SchemaComparer.Compare(Schema, actual, compareIndexing: false));
    }

    [Fact]
    public void Undeclared_container_such_as_leases_is_drift()
    {
        var actual = Provisioned();
        actual.Add(new ContainerSnapshot("leases", "/id", null, [], []));
        Assert.Contains("'leases' exists but is not in Schema/schema.json", Assert.Single(SchemaComparer.Compare(Schema, actual)));
    }

    [Fact]
    public void A_typo_container_is_reported_both_as_missing_and_undeclared()
    {
        var actual = Replace(Provisioned(), "products", c => c with { Name = "product" });
        var drift = SchemaComparer.Compare(Schema, actual);
        Assert.Equal(2, drift.Count);
    }

    private static List<ContainerSnapshot> Replace(List<ContainerSnapshot> snapshots, string name, Func<ContainerSnapshot, ContainerSnapshot> change) =>
        snapshots.Select(c => c.Name == name ? change(c) : c).ToList();
}
