namespace IplFranchise.DbMaintenance.Core;

/// <summary>
/// A data migration: it changes document contents (backfill, rename, split, recompute), never containers.
/// Add a subclass under Migrations/ with the next id; it is discovered automatically.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Idempotent by construction: use the set-if-missing and conditional helpers in <see cref="DocumentOperations"/>,
/// not blind overwrites. A migration can be interrupted and run again from the start.</item>
/// <item>Never edit a migration that has run anywhere; add a new one. To undo a change, add a new one.</item>
/// <item>Filter on docKind: containers hold more than one kind of document.</item>
/// <item>Expand, migrate, contract: ship readers that tolerate a missing field before backfilling it.</item>
/// <item>Use string literals for container and property names, so an old migration keeps its meaning.</item>
/// </list>
/// </remarks>
public abstract class Migration
{
    /// <summary>Sortable unique id: four digits, a dash, lower-case words. For example "0002-backfill-limited-edition".</summary>
    public abstract string Id { get; }

    public abstract string Description { get; }

    public abstract Task ApplyAsync(DocumentOperations db, CancellationToken cancellationToken);
}
