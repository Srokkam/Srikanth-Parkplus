namespace IplFranchise.Domain;

public sealed record RuleViolation(string Code, string Detail, int? Limit = null, int? Available = null)
{
    public const string QuantityLimitExceeded = "quantity-limit-exceeded";
    public const string InsufficientStock = "insufficient-stock";
}

public static class CartRules
{
    /// <summary>
    /// Checks the quantity a line would have after a change. Stock is advisory here: nothing is reserved,
    /// checkout makes the real, atomic check.
    /// </summary>
    public static RuleViolation? Check(Cart cart, Product product, int resultingQuantity, int available)
    {
        var isNewLine = cart.Find(product.Id) is null;
        if (isNewLine && cart.Lines.Count >= Limits.MaxCartLines)
        {
            return new(RuleViolation.QuantityLimitExceeded, $"A cart can hold at most {Limits.MaxCartLines} different products.", Limits.MaxCartLines);
        }

        var lineLimit = Math.Min(Limits.MaxQuantityPerLine, product.MaxPerCustomer ?? int.MaxValue);
        if (resultingQuantity > lineLimit)
        {
            return new(RuleViolation.QuantityLimitExceeded, $"At most {lineLimit} of '{product.Name}' can be bought in one order.", lineLimit);
        }

        if (resultingQuantity > available)
        {
            return new(RuleViolation.InsufficientStock, $"Only {Math.Max(available, 0)} of '{product.Name}' left in stock.", Available: Math.Max(available, 0));
        }

        return null;
    }
}
