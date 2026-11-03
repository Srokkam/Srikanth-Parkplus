using System.Text.RegularExpressions;

namespace IplFranchise.DbMaintenance.Core;

/// <summary>Validates migration ids and returns migrations in the order they must run.</summary>
public static partial class MigrationCatalog
{
    public static IReadOnlyList<Migration> Order(IEnumerable<Migration> migrations)
    {
        var list = migrations.ToList();

        var invalid = list.FirstOrDefault(m => !IdPattern().IsMatch(m.Id));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Migration {invalid.GetType().Name} has id '{invalid.Id}'. Use four digits, a dash and lower-case words, e.g. '0003-backfill-something'.");
        }

        var duplicate = list.GroupBy(m => m.Id[..4]).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Migration number {duplicate.Key} is used more than once: {string.Join(", ", duplicate.Select(m => m.Id))}.");
        }

        return list.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    [GeneratedRegex("^[0-9]{4}-[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}
