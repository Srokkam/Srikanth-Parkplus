using IplFranchise.DbMaintenance.Core;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.DbMaintenance.Migrations;

/// <summary>
/// Adds limitedEdition (false) and maxPerCustomer (null) to every product that lacks them, then makes the seed's
/// signed photo a limited edition. Only docKind 'product' is touched, so price audit rows in the same container
/// are left alone.
/// </summary>
public sealed class BackfillLimitedEdition : Migration
{
    public override string Id => "0002-backfill-limited-edition";

    public override string Description => "Set limitedEdition=false and maxPerCustomer=null where missing; mark the seed limited product";

    public override async Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken)
    {
        await db.SetIfMissingAsync("products", "product", "limitedEdition", false, cancellationToken);
        await db.SetIfMissingAsync<int?>("products", "product", "maxPerCustomer", null, cancellationToken);

        // Exists only where the local seed ran; elsewhere this is a no-op. The condition keeps it idempotent
        // and never overrides a product someone has already configured as limited.
        await db.PatchItemIfAsync(
            "products",
            "kkr-signed-photo",
            new PartitionKey("kkr"),
            "FROM c WHERE c.docKind = 'product' AND c.limitedEdition = false",
            [PatchOperation.Set("/limitedEdition", true), PatchOperation.Set("/maxPerCustomer", 1)],
            cancellationToken);
    }
}
