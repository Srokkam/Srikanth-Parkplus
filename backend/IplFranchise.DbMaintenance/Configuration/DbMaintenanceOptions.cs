using System.ComponentModel.DataAnnotations;
using IplFranchise.DbMaintenance.Schema;

namespace IplFranchise.DbMaintenance.Configuration;

/// <summary>
/// Bound from the "CosmosDb" section, the same section name the API uses,
/// so one environment variable (e.g. CosmosDb__ConnectionString) configures both.
/// </summary>
public sealed class DbMaintenanceOptions
{
    public const string SectionName = "CosmosDb";

    /// <summary>
    /// "AccountEndpoint=...;AccountKey=...;" for the emulator, or just "AccountEndpoint=...;" to sign in with
    /// Entra ID (DefaultAzureCredential), which is what real environments should use.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Defaults to the database in Schema/schema.json. Override only for isolated test databases.</summary>
    [Required]
    public string DatabaseName { get; set; } = SchemaSpec.Current.DatabaseName;

    /// <summary>Documents per query page when a migration walks a container.</summary>
    [Range(1, 1000)]
    public int PageSize { get; set; } = 100;

    /// <summary>Concurrent writes per page. The SDK retries 429s; this caps how hard a migration pushes.</summary>
    [Range(1, 64)]
    public int MaxConcurrency { get; set; } = 8;
}
