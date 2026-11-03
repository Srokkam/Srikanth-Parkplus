using IplFranchise.Api.Auth;
using IplFranchise.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace IplFranchise.Api.Controllers;

/// <summary>Hands out a token for any user id. Exists only so the API can be tried locally; it is not there in other environments.</summary>
[ApiController]
[Route("api/dev/token")]
public sealed class DevAuthController(TokenIssuer issuer, IHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DevTokenResponse>(StatusCodes.Status200OK)]
    public ActionResult<DevTokenResponse> Issue(DevTokenRequest request)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var (token, expires) = issuer.Issue(request.UserId, request.Admin);
        return new DevTokenResponse(token, expires);
    }
}
