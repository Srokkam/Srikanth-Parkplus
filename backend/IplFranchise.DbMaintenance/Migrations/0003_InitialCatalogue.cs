using System.Globalization;
using IplFranchise.DbMaintenance.Core;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Migrations;

/// <summary>
/// The store's opening catalogue: four products across four franchises, each with its opening stock and the audit row
/// for that stock. Everything is create-if-absent with fixed ids, so re-running changes nothing and an existing
/// product, stock level or audit row is never overwritten.
/// </summary>
public sealed class InitialCatalogue : Migration
{
    private const string Actor = "migration:0003-initial-catalogue";

    private static readonly OpeningProduct[] Products =
    [
        new("mi", "mi-jersey", "jersey", "MI Jersey", "Official Mumbai Indians replica jersey.", 199900, Stock: 50),
        new("csk", "csk-cap", "cap", "CSK Cap", "Official Chennai Super Kings cap.", 49900, Stock: 100),
        new("rcb", "rcb-flag", "flag", "RCB Flag", "Official Royal Challengers Bengaluru flag.", 29900, Stock: 200),
        new("kkr", "kkr-signed-photo", "signed-photo", "KKR Signed Photo", "Team photograph signed by the squad. Limited edition.", 499900, Stock: 2, MaxPerCustomer: 1),
    ];

    public override string Id => "0003-initial-catalogue";

    public override string Description => "Add the opening catalogue (MI Jersey, CSK Cap, RCB Flag, KKR Signed Photo) with opening stock";

    public override async Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken)
    {
        var tenant = new PartitionKey("default");
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        foreach (var p in Products)
        {
            // Stock and its audit row first, as the API does, so a product never shows up without stock.
            await db.CreateIfAbsentAsync(
                "inventory",
                new { id = p.Id, tenantId = "default", docKind = "stock", schemaVersion = 1, productId = p.Id, available = p.Stock },
                tenant,
                cancellationToken);

            await db.CreateIfAbsentAsync(
                "inventory",
                new
                {
                    id = $"audit:stock:{p.Id}:initial",
                    tenantId = "default",
                    docKind = "audit",
                    productId = p.Id,
                    field = "stock",
                    oldValue = 0L,
                    newValue = (long)p.Stock,
                    delta = (long)p.Stock,
                    reason = "initial stock",
                    actor = Actor,
                    tsUtc = now,
                },
                tenant,
                cancellationToken);

            await db.CreateIfAbsentAsync(
                "products",
                new
                {
                    id = p.Id,
                    tenantId = "default",
                    franchiseId = p.FranchiseId,
                    docKind = "product",
                    schemaVersion = 1,
                    type = p.Type,
                    name = p.Name,
                    nameLower = p.Name.ToLowerInvariant(),
                    description = p.Description,
                    pricePaise = p.PricePaise,
                    currency = "INR",
                    limitedEdition = p.MaxPerCustomer is not null,
                    maxPerCustomer = p.MaxPerCustomer,
                },
                new PartitionKey(p.FranchiseId),
                cancellationToken);
        }
    }

    private sealed record OpeningProduct(
        string FranchiseId, string Id, string Type, string Name, string Description, long PricePaise, int Stock, int? MaxPerCustomer = null);
}
