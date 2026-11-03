using IplFranchise.DbMaintenance.Core;

namespace IplFranchise.DbMaintenance.Migrations;

/// <summary>
/// Records that the containers exist at schema version 1. No data change: the runner only gets here after
/// verify-schema has confirmed every container matches Schema/schema.json.
/// </summary>
public sealed class Baseline : Migration
{
    public override string Id => "0001-baseline";

    public override string Description => "Containers exist at schema version 1 (no data change)";

    public override Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken) => Task.CompletedTask;
}
