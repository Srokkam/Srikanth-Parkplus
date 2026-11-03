using IplFranchise.Api.Contracts;
using IplFranchise.Application;
using Microsoft.AspNetCore.Mvc;

namespace IplFranchise.Api.Controllers;

[ApiController]
[Route("api/v1/franchises")]
public sealed class FranchisesController(CatalogueService catalogue) : ControllerBase
{
    /// <summary>The franchises that have products in the catalogue, by code. Empty until products are added.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FranchiseResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<FranchiseResponse>> List(CancellationToken cancellationToken) =>
        (await catalogue.ListFranchisesAsync(cancellationToken)).Select(code => new FranchiseResponse(code)).ToList();
}
