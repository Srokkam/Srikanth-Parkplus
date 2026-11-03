using System.Globalization;

namespace IplFranchise.Domain;

/// <summary>
/// One fixed-width UTC format for stored instants, so string comparison in Cosmos DB queries orders them correctly.
/// </summary>
public static class Timestamps
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string ToText(DateTimeOffset value) =>
        value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.ParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
