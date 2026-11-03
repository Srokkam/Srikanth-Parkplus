using System.Net;
using IplFranchise.DbMaintenance.Configuration;
using IplFranchise.DbMaintenance.Schema;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IplFranchise.DbMaintenance.Core;

public enum MigrateOutcome
{
    Succeeded,
    Failed,

    /// <summary>Another runner holds a migration; nothing after it was attempted.</summary>
    Busy,
}

public sealed record MigrateResult(MigrateOutcome Outcome, int Applied, int AlreadyCompleted);

/// <summary>
/// Applies pending migrations in id order. Per migration: read its record; skip if completed; claim it by creating
/// a running record (a conflict means another runner won) or by taking over a failed or stale one conditionally on
/// its ETag; apply; then mark it completed, again conditionally. A failure is recorded and stops the run.
/// </summary>
/// <remarks>
/// A migration that runs longer than <see cref="ClaimPolicy.StaleAfter"/> can be taken over by a second runner;
/// the first then fails to mark it completed (ETag mismatch) and reports that instead of claiming success.
/// </remarks>
public sealed class MigrationRunner(
    CosmosClient client,
    IOptions<DbMaintenanceOptions> options,
    IEnumerable<Migration> migrations,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger logger = loggerFactory.CreateLogger<MigrationRunner>();

    public async Task<MigrateResult> MigrateAsync(CancellationToken cancellationToken)
    {
        var database = client.GetDatabase(options.Value.DatabaseName);
        var records = database.GetContainer(SchemaSpec.MigrationsContainer);
        var operations = new DocumentOperations(database, options.Value, loggerFactory.CreateLogger<DocumentOperations>());
        int applied = 0, alreadyCompleted = 0;

        foreach (var migration in MigrationCatalog.Order(migrations))
        {
            var (record, etag) = await ReadRecordAsync(records, migration.Id, cancellationToken);
            var now = UtcNow();

            switch (ClaimPolicy.Decide(record, now))
            {
                case ClaimDecision.Skip:
                    logger.LogInformation("{Id}: already completed {CompletedUtc:u}", migration.Id, record!.CompletedUtc);
                    alreadyCompleted++;
                    continue;

                case ClaimDecision.Busy:
                    logger.LogWarning("{Id}: another runner has been applying it since {StartedUtc:u}; stopping", migration.Id, record!.StartedUtc);
                    return new MigrateResult(MigrateOutcome.Busy, applied, alreadyCompleted);

                case ClaimDecision.Create:
                    etag = await TryCreateAsync(records, Running(migration, now), cancellationToken);
                    break;

                case ClaimDecision.TakeOver:
                    logger.LogWarning("{Id}: taking over a {Status} attempt from {StartedUtc:u}", migration.Id, record!.Status, record.StartedUtc);
                    etag = await TryReplaceAsync(records, Running(migration, now), etag!, cancellationToken);
                    break;
            }

            if (etag is null)
            {
                logger.LogWarning("{Id}: another runner claimed it first; stopping", migration.Id);
                return new MigrateResult(MigrateOutcome.Busy, applied, alreadyCompleted);
            }

            logger.LogInformation("{Id}: applying - {Description}", migration.Id, migration.Description);

            try
            {
                await migration.ApplyAsync(operations, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Id}: failed; it will be retried from the start on the next run", migration.Id);
                var failed = Running(migration, now) with { Status = MigrationStatus.Failed, Error = ex.Message };
                // Record the failure even when cancelled, so the next run doesn't wait for the record to go stale.
                await TryReplaceAsync(records, failed, etag, CancellationToken.None);
                return new MigrateResult(MigrateOutcome.Failed, applied, alreadyCompleted);
            }

            var completed = Running(migration, now) with { Status = MigrationStatus.Completed, CompletedUtc = UtcNow() };
            if (await TryReplaceAsync(records, completed, etag, cancellationToken) is null)
            {
                logger.LogError("{Id}: another runner took it over while it was running; not marking it completed", migration.Id);
                return new MigrateResult(MigrateOutcome.Failed, applied, alreadyCompleted);
            }

            logger.LogInformation("{Id}: completed", migration.Id);
            applied++;
        }

        logger.LogInformation("Migrations: {Applied} applied, {AlreadyCompleted} already completed", applied, alreadyCompleted);
        return new MigrateResult(MigrateOutcome.Succeeded, applied, alreadyCompleted);
    }

    /// <summary>Every known migration with its record (null when it has never run).</summary>
    public async Task<IReadOnlyList<(Migration Migration, MigrationRecord? Record)>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var records = client.GetDatabase(options.Value.DatabaseName).GetContainer(SchemaSpec.MigrationsContainer);
        var result = new List<(Migration, MigrationRecord?)>();

        foreach (var migration in MigrationCatalog.Order(migrations))
        {
            var (record, _) = await ReadRecordAsync(records, migration.Id, cancellationToken);
            result.Add((migration, record));
        }

        return result;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static MigrationRecord Running(Migration migration, DateTime startedUtc) =>
        new(migration.Id, MigrationStatus.Running, migration.Description, startedUtc, CompletedUtc: null, Error: null);

    private static async Task<(MigrationRecord? Record, string? ETag)> ReadRecordAsync(
        Container records,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await records.ReadItemAsync<MigrationRecord>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    /// <returns>The new ETag, or null if another runner created the record first.</returns>
    private static async Task<string?> TryCreateAsync(Container records, MigrationRecord record, CancellationToken cancellationToken)
    {
        try
        {
            var response = await records.CreateItemAsync(record, new PartitionKey(record.Id), cancellationToken: cancellationToken);
            return response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    /// <returns>The new ETag, or null if the record changed since <paramref name="etag"/> was read.</returns>
    private static async Task<string?> TryReplaceAsync(Container records, MigrationRecord record, string etag, CancellationToken cancellationToken)
    {
        try
        {
            var response = await records.ReplaceItemAsync(
                record, record.Id, new PartitionKey(record.Id), new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);
            return response.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return null;
        }
    }
}
