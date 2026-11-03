using System.Net;
using IplFranchise.DbMaintenance.Core;
using IplFranchise.DbMaintenance.Migrations;
using IplFranchise.DbMaintenance.Schema;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Tests.Emulator;

/// <summary>The verification gates that need a database (section 5 of the reference), run against the emulator.</summary>
public class MaintenanceEmulatorTests(EmulatorFixture emulator) : IClassFixture<EmulatorFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Second_setup_run_creates_and_changes_nothing()
    {
        await using var db = emulator.CreateDatabase();

        Assert.Equal(1 + SchemaSpec.Current.Containers.Count, await db.Get<LocalProvisioner>().ProvisionAsync(Token));
        Assert.Equal(new MigrateResult(MigrateOutcome.Succeeded, 3, 0), await db.Get<MigrationRunner>().MigrateAsync(Token));
        var afterFirstRun = await db.SnapshotAsync();

        Assert.Equal(0, await db.Get<LocalProvisioner>().ProvisionAsync(Token));
        Assert.Equal(new MigrateResult(MigrateOutcome.Succeeded, 0, 3), await db.Get<MigrationRunner>().MigrateAsync(Token));

        Assert.Equal(afterFirstRun, await db.SnapshotAsync());
        Assert.Empty(await db.Get<SchemaVerifier>().VerifyAsync(Token));
    }

    [Fact]
    public async Task Each_migration_run_twice_gives_the_same_documents()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);
        await InsertPre0002DocumentsAsync(db);
        var operations = db.Operations();

        foreach (var migration in MigrationCatalog.Order(db.Get<IEnumerable<Migration>>()))
        {
            await migration.ApplyAsync(operations, Token);
            var once = await db.SnapshotAsync();

            await migration.ApplyAsync(operations, Token);
            Assert.Equal(once, await db.SnapshotAsync());
        }
    }

    [Fact]
    public async Task Backfill_sets_missing_fields_only_on_products_and_marks_the_limited_product()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);
        await InsertPre0002DocumentsAsync(db);

        await new BackfillLimitedEdition().ApplyAsync(db.Operations(), Token);

        var jersey = (await db.ReadAsync("products", "csk-jersey", "csk"))!;
        Assert.False(jersey["limitedEdition"]!.GetValue<bool>());
        Assert.True(jersey.ContainsKey("maxPerCustomer"));
        Assert.Null(jersey["maxPerCustomer"]);

        var photo = (await db.ReadAsync("products", "kkr-signed-photo", "kkr"))!;
        Assert.True(photo["limitedEdition"]!.GetValue<bool>());
        Assert.Equal(1, photo["maxPerCustomer"]!.GetValue<int>());

        var alreadyConfigured = (await db.ReadAsync("products", "rcb-signed-bat", "rcb"))!;
        Assert.Equal(5, alreadyConfigured["maxPerCustomer"]!.GetValue<int>());

        var audit = (await db.ReadAsync("products", "audit:7f3a", "csk"))!;
        Assert.False(audit.ContainsKey("limitedEdition"));
        Assert.False(audit.ContainsKey("maxPerCustomer"));
    }

    [Fact]
    public async Task Verify_reports_drift_and_provision_never_modifies_an_existing_container()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);

        // Someone recreated carts without TTL and added the vestigial leases container.
        await db.Database.GetContainer("carts").DeleteContainerAsync(cancellationToken: Token);
        await db.Database.CreateContainerAsync(new ContainerProperties("carts", "/userId"), cancellationToken: Token);
        await db.Database.CreateContainerAsync(new ContainerProperties("leases", "/id"), cancellationToken: Token);

        Assert.Equal(0, await db.Get<LocalProvisioner>().ProvisionAsync(Token));

        var drift = await db.Get<SchemaVerifier>().VerifyAsync(Token);
        Assert.Equal(2, drift.Count);
        Assert.Contains(drift, d => d.Contains("'carts': default TTL is off"));
        Assert.Contains(drift, d => d.Contains("'leases' exists but is not in Schema/schema.json"));
    }

    [Fact]
    public async Task Runner_stops_when_another_runner_holds_a_fresh_claim()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);
        var claim = new MigrationRecord("0001-baseline", MigrationStatus.Running, "held elsewhere", DateTime.UtcNow.AddMinutes(-1), null, null);
        await db.Database.GetContainer("migrations").CreateItemAsync(claim, cancellationToken: Token);

        var result = await db.Get<MigrationRunner>().MigrateAsync(Token);

        Assert.Equal(new MigrateResult(MigrateOutcome.Busy, 0, 0), result);
        Assert.Equal(MigrationStatus.Running, (await db.ReadAsync("migrations", "0001-baseline", "0001-baseline"))!["status"]!.GetValue<string>());
        Assert.Null(await db.ReadAsync("migrations", "0002-backfill-limited-edition", "0002-backfill-limited-edition"));
    }

    [Fact]
    public async Task Runner_takes_over_stale_and_failed_records()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);
        var migrations = db.Database.GetContainer("migrations");
        await migrations.CreateItemAsync(
            new MigrationRecord("0001-baseline", MigrationStatus.Running, "crashed", DateTime.UtcNow.AddMinutes(-20), null, null), cancellationToken: Token);
        await migrations.CreateItemAsync(
            new MigrationRecord("0002-backfill-limited-edition", MigrationStatus.Failed, "failed", DateTime.UtcNow.AddMinutes(-5), null, "boom"), cancellationToken: Token);

        // Both are taken over, and 0003 (never started) runs after them.
        Assert.Equal(new MigrateResult(MigrateOutcome.Succeeded, 3, 0), await db.Get<MigrationRunner>().MigrateAsync(Token));

        foreach (var id in new[] { "0001-baseline", "0002-backfill-limited-edition", "0003-initial-catalogue" })
        {
            var record = (await db.ReadAsync("migrations", id, id))!;
            Assert.Equal(MigrationStatus.Completed, record["status"]!.GetValue<string>());
            Assert.Null(record["error"]);
            Assert.EndsWith("Z", record["completedUtc"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Failure_is_recorded_stops_the_run_and_is_retried_next_time()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);
        var flaky = new FailsOnceMigration();
        var later = new CountingMigration("0003-after-flaky");

        Assert.Equal(new MigrateResult(MigrateOutcome.Failed, 0, 0), await db.Runner(flaky, later).MigrateAsync(Token));
        var failed = (await db.ReadAsync("migrations", flaky.Id, flaky.Id))!;
        Assert.Equal(MigrationStatus.Failed, failed["status"]!.GetValue<string>());
        Assert.Equal("simulated failure", failed["error"]!.GetValue<string>());
        Assert.Equal(0, later.Runs);

        Assert.Equal(new MigrateResult(MigrateOutcome.Succeeded, 2, 0), await db.Runner(flaky, later).MigrateAsync(Token));
        Assert.Equal(MigrationStatus.Completed, (await db.ReadAsync("migrations", flaky.Id, flaky.Id))!["status"]!.GetValue<string>());
        Assert.Equal(1, later.Runs);
    }

    [Fact]
    public async Task Transactional_batch_with_a_failing_conditional_patch_applies_nothing()
    {
        await using var db = emulator.CreateDatabase();
        await db.Get<LocalProvisioner>().ProvisionAsync(Token);
        var inventory = db.Database.GetContainer("inventory");
        var tenant = new PartitionKey("default");
        await inventory.CreateItemAsync(
            new { id = "kkr-signed-photo", tenantId = "default", docKind = "stock", schemaVersion = 1, productId = "kkr-signed-photo", available = 2 },
            tenant,
            cancellationToken: Token);

        // Checkout for 3 when only 2 are available: the stock patch's condition fails.
        using var rejected = await CheckoutBatch(inventory, "ord-1", quantity: 3).ExecuteAsync(Token);

        Assert.False(rejected.IsSuccessStatusCode);
        Assert.Null(await db.ReadAsync("inventory", "deduct:ord-1", "default"));
        Assert.Null(await db.ReadAsync("inventory", "audit:ord-1", "default"));
        Assert.Equal(2, (await db.ReadAsync("inventory", "kkr-signed-photo", "default"))!["available"]!.GetValue<int>());

        // The same batch shape succeeds when the condition holds...
        using var accepted = await CheckoutBatch(inventory, "ord-2", quantity: 1).ExecuteAsync(Token);
        Assert.True(accepted.IsSuccessStatusCode);
        Assert.Equal(1, (await db.ReadAsync("inventory", "kkr-signed-photo", "default"))!["available"]!.GetValue<int>());

        // ...and repeating it for the same order conflicts on the marker id, so stock is never deducted twice.
        using var duplicate = await CheckoutBatch(inventory, "ord-2", quantity: 1).ExecuteAsync(Token);
        Assert.False(duplicate.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Single(r => r.StatusCode != HttpStatusCode.FailedDependency).StatusCode);
        Assert.Equal(1, (await db.ReadAsync("inventory", "kkr-signed-photo", "default"))!["available"]!.GetValue<int>());
    }

    private static TransactionalBatch CheckoutBatch(Container inventory, string orderId, int quantity) =>
        inventory.CreateTransactionalBatch(new PartitionKey("default"))
            .CreateItem(new
            {
                id = $"deduct:{orderId}",
                tenantId = "default",
                docKind = "deduct",
                orderId,
                userId = "asha",
                createdUtc = "2026-10-28T10:15:00Z",
                orderCreated = false,
            })
            .PatchItem(
                "kkr-signed-photo",
                [PatchOperation.Increment("/available", -quantity)],
                new TransactionalBatchPatchItemRequestOptions { FilterPredicate = $"FROM c WHERE c.available >= {quantity}" })
            .CreateItem(new
            {
                id = $"audit:{orderId}",
                tenantId = "default",
                docKind = "audit",
                productId = "kkr-signed-photo",
                field = "stock",
                delta = -quantity,
                reason = $"order:{orderId}",
                actor = "asha",
                tsUtc = "2026-10-28T10:15:00Z",
            });

    /// <summary>Products as they looked before migration 0002, plus a price audit row in the same partition.</summary>
    private static async Task InsertPre0002DocumentsAsync(TestDatabase db)
    {
        var products = db.Database.GetContainer("products");

        await products.CreateItemAsync(LegacyProduct("csk-jersey", "csk", "jersey", "CSK Jersey", 249900), cancellationToken: Token);
        await products.CreateItemAsync(LegacyProduct("kkr-signed-photo", "kkr", "signed-photo", "KKR Signed Photo", 999900), cancellationToken: Token);
        await products.CreateItemAsync(LegacyProduct("rcb-signed-bat", "rcb", "other", "RCB Signed Bat", 1999900), cancellationToken: Token);
        await products.PatchItemAsync<object>(
            "rcb-signed-bat", new PartitionKey("rcb"),
            [PatchOperation.Set("/limitedEdition", true), PatchOperation.Set("/maxPerCustomer", 5)], cancellationToken: Token);
        await products.CreateItemAsync(
            new
            {
                id = "audit:7f3a",
                franchiseId = "csk",
                docKind = "audit",
                productId = "csk-jersey",
                field = "price",
                oldValue = 229900L,
                newValue = 249900L,
                reason = "seasonal increase",
                actor = "admin-1",
                tsUtc = "2026-10-28T10:15:00Z",
            },
            new PartitionKey("csk"),
            cancellationToken: Token);
    }

    private static LegacyProductDocument LegacyProduct(string id, string franchiseId, string type, string name, long pricePaise) =>
        new(id, "default", franchiseId, "product", 1, type, name, name.ToLowerInvariant(), $"{name}.", pricePaise, "INR");

    private sealed record LegacyProductDocument(
        string Id,
        string TenantId,
        string FranchiseId,
        string DocKind,
        int SchemaVersion,
        string Type,
        string Name,
        string NameLower,
        string Description,
        long PricePaise,
        string Currency);

    private sealed class FailsOnceMigration : Migration
    {
        private bool failed;

        public override string Id => "0001-fails-once";

        public override string Description => "Throws on its first run";

        public override Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken)
        {
            if (failed)
            {
                return Task.CompletedTask;
            }

            failed = true;
            throw new InvalidOperationException("simulated failure");
        }
    }

    private sealed class CountingMigration(string id) : Migration
    {
        public int Runs { get; private set; }

        public override string Id => id;

        public override string Description => "Counts its runs";

        public override Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.CompletedTask;
        }
    }
}
