using IplFranchise.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IplFranchise.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController(HealthCheckService healthCheckService) : ControllerBase
{
    /// <summary>
    /// Confirms the API is up. Does not touch any dependency.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("Healthy", "IplFranchise.Api", DateTimeOffset.UtcNow));

    /// <summary>
    /// Runs the database health checks (Cosmos DB connectivity). Returns 503 when unhealthy.
    /// </summary>
    [HttpGet("db")]
    [ProducesResponseType<DependencyHealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<DependencyHealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DependencyHealthResponse>> GetDatabaseHealth(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("db"), cancellationToken);

        var response = new DependencyHealthResponse(
            report.Status.ToString(),
            report.TotalDuration,
            report.Entries
                .Select(e => new HealthCheckEntry(e.Key, e.Value.Status.ToString(), e.Value.Description, e.Value.Duration))
                .ToList());

        return report.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
