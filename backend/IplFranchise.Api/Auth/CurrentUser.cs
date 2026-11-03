using System.Security.Claims;
using System.Text.RegularExpressions;
using IplFranchise.Api.Http;
using IplFranchise.Application;

namespace IplFranchise.Api.Auth;

public static class Policies
{
    public const string Admin = "Admin";
}

internal static class CurrentUser
{
    // The id ends up inside Cosmos DB document ids, which cannot contain / \ ? or #.
    private static readonly Regex SafeId = new("^[A-Za-z0-9_.@-]{1,64}$", RegexOptions.Compiled);

    public static string Id(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue("sub");
        return id is not null && SafeId.IsMatch(id)
            ? id
            : throw new ApiProblemException(StatusCodes.Status401Unauthorized, "unauthorized", "The token has no usable subject.");
    }
}
