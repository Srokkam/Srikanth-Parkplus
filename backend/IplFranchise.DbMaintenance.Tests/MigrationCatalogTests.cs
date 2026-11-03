using IplFranchise.DbMaintenance.Core;

namespace IplFranchise.DbMaintenance.Tests;

public class MigrationCatalogTests
{
    [Fact]
    public void Project_migrations_are_the_ones_the_contract_requires_in_order()
    {
        var migrations = typeof(Migration).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(Migration)))
            .Select(t => (Migration)Activator.CreateInstance(t)!);

        Assert.Equal(["0001-baseline", "0002-backfill-limited-edition", "0003-initial-catalogue"], MigrationCatalog.Order(migrations).Select(m => m.Id));
    }

    [Fact]
    public void Orders_by_id() =>
        Assert.Equal(
            ["0001-a", "0002-b", "0010-c"],
            MigrationCatalog.Order([new TestMigration("0010-c"), new TestMigration("0001-a"), new TestMigration("0002-b")]).Select(m => m.Id));

    [Theory]
    [InlineData("1-baseline")]
    [InlineData("0003_backfill")]
    [InlineData("0003-Backfill")]
    [InlineData("0003-back/fill")]
    [InlineData("0003-")]
    public void Rejects_ids_that_are_not_sortable_lower_case_words(string id) =>
        Assert.Throws<InvalidOperationException>(() => MigrationCatalog.Order([new TestMigration(id)]));

    [Fact]
    public void Rejects_two_migrations_with_the_same_number() =>
        Assert.Throws<InvalidOperationException>(() =>
            MigrationCatalog.Order([new TestMigration("0003-one-thing"), new TestMigration("0003-another-thing")]));

    private sealed class TestMigration(string id) : Migration
    {
        public override string Id => id;

        public override string Description => "test";

        public override Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
