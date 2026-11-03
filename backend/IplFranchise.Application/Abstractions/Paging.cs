using System.Security.Cryptography;
using System.Text;
using IplFranchise.Domain;

namespace IplFranchise.Application;

internal static class Paging
{
    public static int ValidatePageSize(int? requested)
    {
        var size = requested ?? Limits.DefaultPageSize;
        if (size is < 1 or > Limits.MaxPageSize)
        {
            throw new ValidationFailedException("pageSize", $"Page size must be between 1 and {Limits.MaxPageSize}.");
        }

        return size;
    }

    /// <summary>A token is only valid for the filters it was issued for.</summary>
    public static string Scope(params object?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts.Select(p => p?.ToString() ?? string.Empty)))));

    public static string? Open(IContinuationTokenProtector tokens, string? token, string scope) =>
        string.IsNullOrEmpty(token) ? null : tokens.Unprotect(token, scope);

    public static string? Seal(IContinuationTokenProtector tokens, string? rawToken, string scope) =>
        rawToken is null ? null : tokens.Protect(rawToken, scope);
}
