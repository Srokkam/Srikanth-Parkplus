namespace IplFranchise.Api.Models;

public sealed record HealthResponse(string Status, string Service, DateTimeOffset Timestamp);

public sealed record DependencyHealthResponse(string Status, TimeSpan TotalDuration, IReadOnlyList<HealthCheckEntry> Checks);

public sealed record HealthCheckEntry(string Name, string Status, string? Description, TimeSpan Duration);
