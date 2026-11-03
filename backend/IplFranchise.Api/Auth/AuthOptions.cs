using System.ComponentModel.DataAnnotations;

namespace IplFranchise.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Required]
    public string Issuer { get; set; } = "ipl-franchise-dev";

    [Required]
    public string Audience { get; set; } = "ipl-franchise-api";

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;
}
