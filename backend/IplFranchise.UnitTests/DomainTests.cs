using IplFranchise.Domain;

namespace IplFranchise.UnitTests;

public class StockPolicyTests
{
    [Theory]
    [InlineData(-1, StockStatus.OutOfStock)]
    [InlineData(0, StockStatus.OutOfStock)]
    [InlineData(1, StockStatus.LowStock)]
    [InlineData(5, StockStatus.LowStock)]
    [InlineData(6, StockStatus.InStock)]
    public void Status_changes_at_the_documented_boundaries(int available, StockStatus expected) =>
        Assert.Equal(expected, StockPolicy.StatusFor(available));
}

public class CartRulesTests
{
    private static readonly Product Cap = Catalogue.Cap;

    [Theory]
    [InlineData(1, 100, false)]
    [InlineData(10, 100, false)]
    [InlineData(11, 100, true)]
    public void A_line_holds_between_one_and_ten(int quantity, int available, bool violates) =>
        Assert.Equal(violates, CartRules.Check(Cart.Empty("asha"), Cap, quantity, available) is not null);

    [Fact]
    public void Quantity_may_equal_the_last_unit_but_not_exceed_it()
    {
        Assert.Null(CartRules.Check(Cart.Empty("asha"), Cap, 1, 1));

        var violation = CartRules.Check(Cart.Empty("asha"), Cap, 2, 1);
        Assert.Equal(RuleViolation.InsufficientStock, violation!.Code);
        Assert.Equal(1, violation.Available);
    }

    [Fact]
    public void Nothing_is_available_when_stock_is_zero() =>
        Assert.Equal(RuleViolation.InsufficientStock, CartRules.Check(Cart.Empty("asha"), Cap, 1, 0)!.Code);

    [Fact]
    public void A_per_customer_limit_below_ten_wins()
    {
        var photo = Catalogue.SignedPhoto;

        Assert.Null(CartRules.Check(Cart.Empty("asha"), photo, 1, 2));
        Assert.Equal(1, CartRules.Check(Cart.Empty("asha"), photo, 2, 2)!.Limit);
    }

    [Fact]
    public void A_cart_holds_at_most_twenty_different_products()
    {
        var lines = Enumerable.Range(1, Limits.MaxCartLines).Select(i => new CartLine($"p{i}", "csk", 1, 100)).ToList();
        var full = new Cart("asha", lines);

        Assert.Equal(Limits.MaxCartLines, CartRules.Check(full, Cap, 1, 100)!.Limit);
        Assert.Null(CartRules.Check(full, Cap with { Id = "p1" }, 2, 100));
    }
}

public class CartPricingTests
{
    [Fact]
    public void Totals_follow_the_walkthrough()
    {
        var cart = new Cart("asha",
        [
            new CartLine("mi-jersey", "mi", 2, 199900),
            new CartLine("csk-cap", "csk", 3, 49900),
            new CartLine("kkr-signed-photo", "kkr", 1, 499900),
        ]);

        var view = CartPricing.Build(cart, Catalogue.ByIdAll, Catalogue.StockAll);

        Assert.Equal(399800, view.Lines[0].LineTotalPaise);
        Assert.Equal(149700, view.Lines[1].LineTotalPaise);
        Assert.Equal(1049400, view.TotalPaise);
        Assert.False(view.RequiresAcceptance);
        Assert.False(view.HasBlockingIssues);
    }

    [Fact]
    public void A_price_change_is_flagged_and_needs_acceptance()
    {
        var cart = new Cart("asha", [new CartLine("csk-cap", "csk", 3, 49900)]);
        var raised = new Dictionary<string, Product> { ["csk-cap"] = Catalogue.Cap with { PricePaise = 54900 } };

        var view = CartPricing.Build(cart, raised, Catalogue.StockAll);

        Assert.True(view.Lines[0].PriceChanged);
        Assert.True(view.RequiresAcceptance);
        Assert.Equal(149700, view.AcceptedSubtotalPaise);
        Assert.Equal(164700, view.TotalPaise);
    }

    [Theory]
    [InlineData(0, LineStock.Unavailable)]
    [InlineData(2, LineStock.Insufficient)]
    [InlineData(3, LineStock.Ok)]
    public void Stock_state_depends_on_what_is_left(int available, LineStock expected)
    {
        var cart = new Cart("asha", [new CartLine("csk-cap", "csk", 3, 49900)]);
        var stock = new Dictionary<string, int> { ["csk-cap"] = available };

        var view = CartPricing.Build(cart, Catalogue.ByIdAll, stock);

        Assert.Equal(expected, view.Lines[0].Stock);
        Assert.Equal(expected != LineStock.Ok, view.HasBlockingIssues);
    }

    [Fact]
    public void A_product_that_no_longer_exists_blocks_checkout()
    {
        var cart = new Cart("asha", [new CartLine("gone", "csk", 1, 100)]);

        var view = CartPricing.Build(cart, new Dictionary<string, Product>(), new Dictionary<string, int>());

        Assert.Equal(LineStock.Unavailable, view.Lines[0].Stock);
    }
}

public class CheckoutRulesTests
{
    private static CartView ViewOf(long accepted, long current, int quantity, int available)
    {
        var cart = new Cart("asha", [new CartLine("csk-cap", "csk", quantity, accepted)]);
        var products = new Dictionary<string, Product> { ["csk-cap"] = Catalogue.Cap with { PricePaise = current } };
        return CartPricing.Build(cart, products, new Dictionary<string, int> { ["csk-cap"] = available });
    }

    [Fact]
    public void A_clean_cart_with_the_right_total_proceeds()
    {
        var view = ViewOf(49900, 49900, 2, 10);

        Assert.IsType<CheckoutDecision.Proceed>(CheckoutRules.Decide(view, 99800));
    }

    [Fact]
    public void A_stale_total_is_rejected_with_the_current_one()
    {
        var view = ViewOf(49900, 49900, 2, 10);

        var decision = Assert.IsType<CheckoutDecision.PriceChanged>(CheckoutRules.Decide(view, 1));
        Assert.Equal(99800, decision.CurrentTotalPaise);
    }

    [Fact]
    public void An_unaccepted_price_change_blocks_even_when_the_client_sends_the_new_total()
    {
        var view = ViewOf(49900, 54900, 1, 10);

        Assert.IsType<CheckoutDecision.PriceChanged>(CheckoutRules.Decide(view, 54900));
    }

    [Fact]
    public void Every_failing_line_is_reported()
    {
        var cart = new Cart("ravi", [new CartLine("csk-cap", "csk", 3, 49900), new CartLine("kkr-signed-photo", "kkr", 1, 499900)]);
        var stock = new Dictionary<string, int> { ["csk-cap"] = 2, ["kkr-signed-photo"] = 0 };
        var view = CartPricing.Build(cart, Catalogue.ByIdAll, stock);

        var decision = Assert.IsType<CheckoutDecision.Blocked>(CheckoutRules.Decide(view, view.TotalPaise));

        Assert.Equal(
            [FailedLine.InsufficientStock, FailedLine.OutOfStock],
            decision.Lines.Select(l => l.Reason).ToArray());
    }
}

public class OrderTests
{
    [Fact]
    public void An_order_keeps_the_name_and_price_it_was_created_with()
    {
        var cart = new Cart("asha", [new CartLine("csk-cap", "csk", 3, 49900)]);
        var view = CartPricing.Build(cart, Catalogue.ByIdAll, Catalogue.StockAll);

        var order = Order.FromView("o-1", "asha", DateTimeOffset.UnixEpoch, view);

        Assert.Equal(149700, order.TotalPaise);
        Assert.Equal("CSK Cap", order.Lines[0].Name);
        Assert.Equal(49900, order.Lines[0].UnitPricePaise);
    }
}

internal static class Catalogue
{
    public static Product Cap { get; } = new("csk-cap", "csk", "CSK Cap", "cap", "Official cap.", 49900, false, null);

    public static Product Jersey { get; } = new("mi-jersey", "mi", "MI Jersey", "jersey", "Official jersey.", 199900, false, null);

    public static Product SignedPhoto { get; } = new("kkr-signed-photo", "kkr", "KKR Signed Photo", "signed-photo", "Signed.", 499900, true, 1);

    public static IReadOnlyDictionary<string, Product> ByIdAll { get; } =
        new[] { Cap, Jersey, SignedPhoto }.ToDictionary(p => p.Id);

    public static IReadOnlyDictionary<string, int> StockAll { get; } =
        new Dictionary<string, int> { ["csk-cap"] = 100, ["mi-jersey"] = 50, ["kkr-signed-photo"] = 2 };
}

public class CatalogueRulesTests
{
    [Theory]
    [InlineData("csk", true)]
    [InlineData("mi", true)]
    [InlineData("pbks", true)]
    [InlineData("c", false)]
    [InlineData("CSK", false)]
    [InlineData("csk1", false)]
    [InlineData("averylongcode", false)]
    [InlineData(null, false)]
    public void Franchise_codes_are_2_to_10_lower_case_letters(string? code, bool valid) =>
        Assert.Equal(valid, FranchiseCodes.IsValid(code));

    [Theory]
    [InlineData("csk-cap", true)]
    [InlineData("csk-signed-photo-2026", true)]
    [InlineData("csk", false)]
    [InlineData("csk-", false)]
    [InlineData("csk--cap", false)]
    [InlineData("mi-cap", false)]
    [InlineData("csk-cap#1", false)]
    public void Product_ids_are_slugs_prefixed_with_the_franchise_code(string id, bool valid) =>
        Assert.Equal(valid, ProductRules.IsValidId(id, "csk"));
}
