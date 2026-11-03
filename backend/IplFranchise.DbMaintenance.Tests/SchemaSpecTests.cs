using System.Text.RegularExpressions;
using IplFranchise.DbMaintenance.Schema;

namespace IplFranchise.DbMaintenance.Tests;

/// <summary>Pins Schema/schema.json to the container contract (section 2 of the reference).</summary>
public class SchemaSpecTests
{
    private static readonly SchemaDefinition Schema = SchemaSpec.Current;

    [Fact]
    public void Database_is_ipl_store() => Assert.Equal("ipl-store", Schema.DatabaseName);

    [Fact]
    public void Declares_exactly_the_contract_containers() =>
        Assert.Equal(["inventory", "products", "carts", "orders", "migrations"], Schema.Containers.Select(c => c.Name));

    [Fact]
    public void Leases_is_deliberately_not_declared() =>
        // Nothing uses a change-feed processor (checkout uses a sweeper), so the vestigial container is dropped.
        Assert.DoesNotContain(Schema.Containers, c => c.Name == "leases");

    [Theory]
    [InlineData("inventory", "/tenantId", null)]
    [InlineData("products", "/franchiseId", null)]
    [InlineData("carts", "/userId", 2_592_000)]
    [InlineData("orders", "/userId", null)]
    [InlineData("migrations", "/id", null)]
    public void Partition_key_and_ttl_match_the_contract(string name, string partitionKeyPath, int? defaultTtlSeconds)
    {
        var container = Container(name);
        Assert.Equal(partitionKeyPath, container.PartitionKeyPath);
        Assert.Equal(defaultTtlSeconds, container.DefaultTtlSeconds);
    }

    [Fact]
    public void Only_carts_has_ttl_so_no_business_data_can_expire() =>
        Assert.Equal(["carts"], Schema.Containers.Where(c => c.DefaultTtlSeconds is not null).Select(c => c.Name));

    [Fact]
    public void Products_excludes_description_and_indexes_name_then_id()
    {
        var products = Container("products");
        Assert.Equal(["/description/?"], products.ExcludedPaths);
        Assert.Equal(
            [new CompositeIndexPath("/nameLower", IndexOrder.Ascending), new CompositeIndexPath("/id", IndexOrder.Ascending)],
            Assert.Single(products.CompositeIndexes));
    }

    [Fact]
    public void Orders_indexes_newest_first()
    {
        Assert.Equal(
            [new CompositeIndexPath("/createdUtc", IndexOrder.Descending), new CompositeIndexPath("/id", IndexOrder.Descending)],
            Assert.Single(Container("orders").CompositeIndexes));
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("carts")]
    [InlineData("migrations")]
    public void Other_containers_use_the_default_indexing_policy(string name)
    {
        var container = Container(name);
        Assert.Empty(container.ExcludedPaths);
        Assert.Empty(container.CompositeIndexes);
    }

    [Fact]
    public void Names_are_lower_case_and_hyphenated()
    {
        Assert.Matches("^[a-z]+(-[a-z]+)*$", Schema.DatabaseName);
        Assert.All(Schema.Containers, c => Assert.Matches("^[a-z]+(-[a-z]+)*$", c.Name));
    }

    [Fact]
    public void Partition_keys_are_single_top_level_paths() =>
        Assert.All(Schema.Containers, c => Assert.Matches("^/[A-Za-z]+$", c.PartitionKeyPath));

    [Fact]
    public void Composite_indexes_have_at_least_two_paths() =>
        Assert.All(Schema.Containers.SelectMany(c => c.CompositeIndexes), index => Assert.True(index.Count >= 2));

    [Fact]
    public void Migration_records_container_is_declared() =>
        Assert.Contains(Schema.Containers, c => c.Name == SchemaSpec.MigrationsContainer && c.PartitionKeyPath == "/id");

    [Fact]
    public void Every_business_container_documents_its_doc_kinds() =>
        Assert.All(Schema.Containers.Where(c => c.Name != SchemaSpec.MigrationsContainer), c => Assert.NotEmpty(c.DocKinds));

    private static ContainerSpec Container(string name) => Schema.Containers.Single(c => c.Name == name);
}
