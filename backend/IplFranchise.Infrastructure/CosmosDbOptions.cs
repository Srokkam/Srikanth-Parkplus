using System.ComponentModel.DataAnnotations;

namespace IplFranchise.Infrastructure;

/// <summary>
/// Bound from the "CosmosDb" section. The API never creates or changes containers:
/// see IplFranchise.DbMaintenance (Schema/schema.json).
/// </summary>
public sealed class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>How long one request to Cosmos DB may take before it fails (and the API answers 503 with Retry-After).</summary>
    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>Throttled (429) requests are retried with the back-off Cosmos DB asks for, up to this many times...</summary>
    [Range(0, 20)]
    public int MaxRetriesOnThrottling { get; set; } = 5;

    /// <summary>...and for no longer than this in total, so a busy database degrades into a fast 503 instead of a hang.</summary>
    [Range(1, 120)]
    public int MaxRetryWaitSeconds { get; set; } = 15;
}

public sealed class ContinuationTokenOptions
{
    public const string SectionName = "ContinuationTokens";

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Set while rotating keys, so tokens signed with the old key stay valid until they expire.</summary>
    public string? PreviousSigningKey { get; set; }

    [Range(1, 1440)]
    public int LifetimeMinutes { get; set; } = 60;
}
