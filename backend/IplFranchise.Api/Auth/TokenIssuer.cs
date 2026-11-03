using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IplFranchise.Api.Auth;

/// <summary>Development only: stands in for a real identity provider so the API can be exercised end to end.</summary>
public sealed class TokenIssuer(IOptions<AuthOptions> options, TimeProvider clock)
{
    public const string AdminRole = "admin";

    public (string Token, DateTimeOffset ExpiresUtc) Issue(string userId, bool admin)
    {
        var settings = options.Value;
        var expires = clock.GetUtcNow().AddHours(8);

        var claims = new Dictionary<string, object> { ["sub"] = userId };
        if (admin)
        {
            claims["role"] = AdminRole;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
