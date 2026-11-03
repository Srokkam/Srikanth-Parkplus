namespace IplFranchise.DbMaintenance.Core;

/// <summary>
/// One document per migration in the "migrations" container (partition key /id).
/// The record is the only proof a migration ran; its _etag (read from the response, not the body)
/// makes claiming it a conditional write so two runners can't both win.
/// </summary>
/// <param name="StartedUtc">When the current or last attempt started. Serialized as ISO 8601 ending in Z.</param>
public sealed record MigrationRecord(
    string Id,
    string Status,
    string Description,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string? Error);

public static class MigrationStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
