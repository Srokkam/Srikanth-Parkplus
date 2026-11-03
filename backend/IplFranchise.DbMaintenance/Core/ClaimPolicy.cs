namespace IplFranchise.DbMaintenance.Core;

public enum ClaimDecision
{
    /// <summary>Already completed.</summary>
    Skip,

    /// <summary>No record yet: create one as running (a conflict means another runner won).</summary>
    Create,

    /// <summary>Failed, or running but stale: replace the record conditionally on its ETag.</summary>
    TakeOver,

    /// <summary>Another runner is applying it right now: stop.</summary>
    Busy,
}

/// <summary>Decides what the runner does with a migration, given its record. Pure, so it is unit tested directly.</summary>
public static class ClaimPolicy
{
    /// <summary>A running record older than this is treated as a crashed runner and can be taken over.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public static ClaimDecision Decide(MigrationRecord? record, DateTime utcNow) => record switch
    {
        null => ClaimDecision.Create,
        { Status: MigrationStatus.Completed } => ClaimDecision.Skip,
        { Status: MigrationStatus.Running } when utcNow - record.StartedUtc < StaleAfter => ClaimDecision.Busy,
        { Status: MigrationStatus.Running or MigrationStatus.Failed } => ClaimDecision.TakeOver,
        _ => throw new InvalidOperationException($"Migration '{record.Id}' has unknown status '{record.Status}'."),
    };
}
