using System.Text.RegularExpressions;

namespace IplFranchise.Domain;

/// <summary>
/// Franchises are not a fixed list: they are whatever franchise codes the products in the catalogue use.
/// This only checks that a code has the right shape, e.g. "csk".
/// </summary>
public static class FranchiseCodes
{
    public const int MinLength = 2;
    public const int MaxLength = 10;

    public static bool IsValid(string? code) =>
        code is { Length: >= MinLength and <= MaxLength } && code.All(char.IsAsciiLetterLower);
}

public static class ProductTypes
{
    public static IReadOnlyList<string> All { get; } = ["jersey", "cap", "flag", "signed-photo", "other"];

    public static bool IsValid(string? type) => type is not null && All.Contains(type);
}

public static partial class ProductRules
{
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;

    /// <summary>
    /// Lower-case words joined by dashes that start with the franchise code, e.g. "csk-cap". The stock record
    /// uses the same id in one shared partition, so the prefix keeps ids unique across franchises.
    /// </summary>
    public static bool IsValidId(string? id, string franchiseId) =>
        id is { Length: > 0 and <= MaxIdLength }
        && IdPattern().IsMatch(id)
        && id.StartsWith(franchiseId + "-", StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}

public sealed record Product(
    string Id,
    string FranchiseId,
    string Name,
    string Type,
    string Description,
    long PricePaise,
    bool LimitedEdition,
    int? MaxPerCustomer);
