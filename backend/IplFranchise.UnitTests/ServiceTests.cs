using IplFranchise.Application;
using IplFranchise.Domain;

namespace IplFranchise.UnitTests;

public class CartServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly FakeStore store = new();
    private readonly CartService carts;

    public CartServiceTests()
    {
        store.Add(Catalogue.Cap, 100);
        store.Add(Catalogue.Jersey, 50);
        store.Add(Catalogue.SignedPhoto, 2);
        carts = new CartService(store, store, store, new CartViewBuilder(store, store));
    }

    [Fact]
    public async Task Adding_a_product_builds_the_cart_total()
    {
        await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("mi", "mi-jersey", 2), Ct);
        var view = await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 3), Ct);

        Assert.Equal(399800 + 149700, view.TotalPaise);
        Assert.Equal(2, view.Lines.Count);
    }

    [Fact]
    public async Task A_repeated_request_id_is_applied_once_and_returns_the_original_result()
    {
        var id = Guid.NewGuid().ToString();
        var item = new AddCartItem("kkr", "kkr-signed-photo", 1);

        var first = await carts.AddAsync("asha", id, item, Ct);
        var second = await carts.AddAsync("asha", id, item, Ct);

        Assert.Equal(first.TotalPaise, second.TotalPaise);
        Assert.Equal(first.Lines.Single(), second.Lines.Single());
        Assert.Equal(1, second.Lines.Single().Quantity);
        Assert.Equal(1, store.CommittedCartWrites);
    }

    [Fact]
    public async Task The_same_request_id_with_a_different_request_is_a_conflict()
    {
        var id = Guid.NewGuid().ToString();
        await carts.AddAsync("asha", id, new AddCartItem("csk", "csk-cap", 1), Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => carts.AddAsync("asha", id, new AddCartItem("csk", "csk-cap", 2), Ct));

        Assert.Equal(ProblemCodes.IdempotencyKeyReused, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Quantities_outside_one_to_ten_are_validation_errors(int quantity) =>
        await Assert.ThrowsAsync<ValidationFailedException>(
            () => carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", quantity), Ct));

    [Fact]
    public async Task Adding_more_than_the_stock_left_is_refused_with_what_is_available()
    {
        store.SetStock("csk-cap", 2);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 3), Ct));

        Assert.Equal(ProblemCodes.InsufficientStock, error.Code);
        Assert.Equal(2, error.Extensions["available"]);
    }

    [Fact]
    public async Task Quantities_add_up_and_the_sum_is_checked_against_the_cap()
    {
        await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 6), Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 5), Ct));

        Assert.Equal(ProblemCodes.QuantityLimitExceeded, error.Code);
    }

    [Fact]
    public async Task A_price_change_is_flagged_and_accepting_clears_it()
    {
        await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 3), Ct);
        store.SetPrice("csk-cap", 54900);

        var flagged = await carts.GetAsync("asha", Ct);
        Assert.True(flagged.RequiresAcceptance);

        var accepted = await carts.AcceptChangesAsync("asha", Guid.NewGuid().ToString(), flagged.TotalPaise, Ct);
        Assert.False(accepted.RequiresAcceptance);
        Assert.Equal(164700, accepted.TotalPaise);
    }

    [Fact]
    public async Task Accepting_a_total_the_customer_did_not_see_is_refused()
    {
        await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 3), Ct);
        store.SetPrice("csk-cap", 54900);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => carts.AcceptChangesAsync("asha", Guid.NewGuid().ToString(), 149700, Ct));

        Assert.Equal(ProblemCodes.PriceChanged, error.Code);
        Assert.Equal(164700L, error.Extensions["currentTotalPaise"]);
    }

    [Fact]
    public async Task Removing_the_last_line_empties_the_cart()
    {
        await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 1), Ct);

        var view = await carts.RemoveAsync("asha", Guid.NewGuid().ToString(), "csk-cap", Ct);

        Assert.Empty(view.Lines);
        Assert.False(store.HasCart("asha"));
    }

    [Fact]
    public async Task Carts_are_per_user()
    {
        await carts.AddAsync("asha", Guid.NewGuid().ToString(), new AddCartItem("csk", "csk-cap", 1), Ct);

        Assert.Empty((await carts.GetAsync("ravi", Ct)).Lines);
    }
}

public class CheckoutServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly FakeStore store = new();
    private readonly CartService carts;
    private readonly CheckoutService checkout;

    public CheckoutServiceTests()
    {
        store.Add(Catalogue.Cap, 100);
        store.Add(Catalogue.Jersey, 50);
        store.Add(Catalogue.SignedPhoto, 1);
        var views = new CartViewBuilder(store, store);
        carts = new CartService(store, store, store, views);
        checkout = new CheckoutService(store, views, store, store, store, store, new FakeClock(DateTimeOffset.UnixEpoch.AddDays(1)));
    }

    private Task Fill(string user, string franchise, string product, int quantity) =>
        carts.AddAsync(user, Guid.NewGuid().ToString(), new AddCartItem(franchise, product, quantity), Ct);

    [Fact]
    public async Task Checkout_creates_the_order_deducts_stock_and_clears_the_cart()
    {
        await Fill("asha", "mi", "mi-jersey", 2);
        await Fill("asha", "csk", "csk-cap", 3);

        var placed = await checkout.PlaceOrderAsync("asha", "order-1", 549500, Ct);

        Assert.True(placed.Created);
        Assert.Equal(549500, placed.Order.TotalPaise);
        Assert.Equal(48, store.Stock("mi-jersey"));
        Assert.Equal(97, store.Stock("csk-cap"));
        Assert.False(store.HasCart("asha"));
    }

    [Fact]
    public async Task A_retried_checkout_returns_the_same_order_and_does_not_deduct_again()
    {
        await Fill("asha", "csk", "csk-cap", 3);

        var first = await checkout.PlaceOrderAsync("asha", "order-1", 149700, Ct);
        var retry = await checkout.PlaceOrderAsync("asha", "order-1", 149700, Ct);

        Assert.False(retry.Created);
        Assert.Equal(first.Order, retry.Order);
        Assert.Equal(97, store.Stock("csk-cap"));
        Assert.Equal(1, store.OrderCount);
    }

    [Fact]
    public async Task A_rejected_order_deducts_nothing_names_the_failing_lines_and_keeps_the_cart()
    {
        await Fill("ravi", "csk", "csk-cap", 3);
        await Fill("ravi", "kkr", "kkr-signed-photo", 1);
        store.SetStock("kkr-signed-photo", 0);

        var error = await Assert.ThrowsAsync<ConflictException>(() => checkout.PlaceOrderAsync("ravi", "order-9", 649600, Ct));

        Assert.Equal(ProblemCodes.OutOfStock, error.Code);
        var failed = Assert.IsAssignableFrom<IReadOnlyList<FailedLine>>(error.Extensions["failedLines"]);
        Assert.Equal("kkr-signed-photo", failed.Single().ProductId);
        Assert.Equal(100, store.Stock("csk-cap"));
        Assert.True(store.HasCart("ravi"));
        Assert.Equal(0, store.OrderCount);
    }

    [Fact]
    public async Task Only_one_of_two_concurrent_checkouts_gets_the_last_unit()
    {
        await Fill("asha", "kkr", "kkr-signed-photo", 1);
        await Fill("ravi", "kkr", "kkr-signed-photo", 1);

        var results = await Task.WhenAll(
            Attempt("asha", "order-a"),
            Attempt("ravi", "order-r"));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(0, store.Stock("kkr-signed-photo"));
        Assert.Equal(1, store.OrderCount);

        async Task<bool> Attempt(string user, string orderId)
        {
            try
            {
                await checkout.PlaceOrderAsync(user, orderId, 499900, Ct);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        }
    }

    [Fact]
    public async Task A_stale_total_is_rejected_before_any_stock_moves()
    {
        await Fill("asha", "csk", "csk-cap", 3);
        store.SetPrice("csk-cap", 54900);

        var error = await Assert.ThrowsAsync<ConflictException>(() => checkout.PlaceOrderAsync("asha", "order-1", 149700, Ct));

        Assert.Equal(ProblemCodes.PriceChanged, error.Code);
        Assert.Equal(100, store.Stock("csk-cap"));
    }

    [Fact]
    public async Task Later_price_changes_do_not_alter_a_placed_order()
    {
        await Fill("asha", "csk", "csk-cap", 3);
        await checkout.PlaceOrderAsync("asha", "order-1", 149700, Ct);

        store.SetPrice("csk-cap", 54900);
        var again = await checkout.PlaceOrderAsync("asha", "order-1", 149700, Ct);

        Assert.Equal(49900, again.Order.Lines.Single().UnitPricePaise);
    }

    [Fact]
    public async Task An_empty_cart_cannot_be_checked_out()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() => checkout.PlaceOrderAsync("asha", "order-1", 0, Ct));

        Assert.Equal(ProblemCodes.CartEmpty, error.Code);
    }

    [Fact]
    public async Task A_limited_edition_can_be_bought_once_per_customer()
    {
        store.SetStock("kkr-signed-photo", 5);
        await Fill("asha", "kkr", "kkr-signed-photo", 1);
        await checkout.PlaceOrderAsync("asha", "order-1", 499900, Ct);

        await Fill("asha", "kkr", "kkr-signed-photo", 1);
        var error = await Assert.ThrowsAsync<ConflictException>(() => checkout.PlaceOrderAsync("asha", "order-2", 499900, Ct));

        Assert.Equal(ProblemCodes.QuantityLimitExceeded, error.Code);
        Assert.Equal(4, store.Stock("kkr-signed-photo"));
    }

    [Fact]
    public async Task A_duplicate_that_arrives_after_the_cart_was_emptied_gets_the_placed_order()
    {
        await Fill("asha", "csk", "csk-cap", 1);
        var first = await checkout.PlaceOrderAsync("asha", "order-1", 49900, Ct);

        // The duplicate looked for the order a moment too early, before the first request had created it.
        var lateOrders = new HidesFirstLookup(store);
        var duplicate = new CheckoutService(
            store, new CartViewBuilder(store, store), store, store, store, lateOrders, new FakeClock(DateTimeOffset.UnixEpoch.AddDays(1)));

        var retry = await duplicate.PlaceOrderAsync("asha", "order-1", 49900, Ct);

        Assert.False(retry.Created);
        Assert.Equal(first.Order, retry.Order);
    }

    private sealed class HidesFirstLookup(IOrderStore inner) : IOrderStore
    {
        private bool hidden;

        public Task<PlacedOrder> CreateIfAbsentAsync(Order order, CancellationToken cancellationToken) =>
            inner.CreateIfAbsentAsync(order, cancellationToken);

        public Task<Order?> GetAsync(string userId, string orderId, CancellationToken cancellationToken)
        {
            if (hidden)
            {
                return inner.GetAsync(userId, orderId, cancellationToken);
            }

            hidden = true;
            return Task.FromResult<Order?>(null);
        }

        public Task<RawPage<Order>> ListAsync(
            string userId, DateTimeOffset? from, DateTimeOffset? to, int pageSize, string? token, CancellationToken cancellationToken) =>
            inner.ListAsync(userId, from, to, pageSize, token, cancellationToken);
    }

    [Fact]
    public async Task Another_customer_cannot_reuse_an_order_id()
    {
        await Fill("asha", "csk", "csk-cap", 1);
        await Fill("ravi", "csk", "csk-cap", 1);
        await checkout.PlaceOrderAsync("asha", "order-1", 49900, Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(() => checkout.PlaceOrderAsync("ravi", "order-1", 49900, Ct));

        Assert.Equal(ProblemCodes.IdempotencyKeyReused, error.Code);
    }
}

public class CheckoutRecoveryTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task An_order_whose_creation_was_interrupted_is_completed_from_the_marker()
    {
        var store = new FakeStore();
        store.Add(Catalogue.Cap, 100);
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var cart = new Cart("asha", [new CartLine("csk-cap", "csk", 3, 49900)]);
        var view = CartPricing.Build(cart, Catalogue.ByIdAll, Catalogue.StockAll);
        var order = Order.FromView("order-1", "asha", clock.Now, view);

        await store.DeductAsync(order, new HashSet<string>(), Ct);
        Assert.Equal(0, store.OrderCount);

        clock.Now = clock.Now.AddMinutes(10);
        var recovery = new CheckoutRecovery(store, store, clock);

        Assert.Equal(1, await recovery.CompleteStaleAsync(TimeSpan.FromMinutes(2), Ct));
        Assert.Equal(1, store.OrderCount);
        Assert.Equal(97, store.Stock("csk-cap"));
        Assert.Equal(0, await recovery.CompleteStaleAsync(TimeSpan.FromMinutes(2), Ct));
    }

    [Fact]
    public async Task A_checkout_that_is_still_in_flight_is_left_alone()
    {
        var store = new FakeStore();
        store.Add(Catalogue.Cap, 100);
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var view = CartPricing.Build(new Cart("asha", [new CartLine("csk-cap", "csk", 1, 49900)]), Catalogue.ByIdAll, Catalogue.StockAll);
        await store.DeductAsync(Order.FromView("order-1", "asha", clock.Now, view), new HashSet<string>(), Ct);

        var recovery = new CheckoutRecovery(store, store, clock);

        Assert.Equal(0, await recovery.CompleteStaleAsync(TimeSpan.FromMinutes(2), Ct));
    }
}

public class PagingTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Page_size_is_capped(int pageSize)
    {
        var service = new CatalogueService(new FakeStore(), new FakeStore(), new PassThroughTokens());

        await Assert.ThrowsAsync<ValidationFailedException>(
            () => service.SearchAsync(new ProductSearchRequest(null, null, null, pageSize, null), Ct));
    }

    [Fact]
    public async Task Unknown_filters_are_validation_errors_listing_every_problem()
    {
        var service = new CatalogueService(new FakeStore(), new FakeStore(), new PassThroughTokens());

        var error = await Assert.ThrowsAsync<ValidationFailedException>(
            () => service.SearchAsync(new ProductSearchRequest(null, "hat", "CSK!", 100, null), Ct));

        Assert.Equal(["franchise", "pageSize", "type"], error.Errors.Keys.Order().ToArray());
    }

    [Fact]
    public async Task A_well_formed_franchise_with_no_products_finds_nothing_instead_of_failing()
    {
        var store = new FakeStore();
        var service = new CatalogueService(store, store, new PassThroughTokens());

        var page = await service.SearchAsync(new ProductSearchRequest(null, null, "dc", null, null), Ct);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task A_token_from_other_filters_is_rejected()
    {
        var service = new CatalogueService(new FakeStore(), new FakeStore(), new PassThroughTokens());

        await Assert.ThrowsAsync<InvalidContinuationTokenException>(
            () => service.SearchAsync(new ProductSearchRequest("cap", null, null, 10, "someone-elses-token"), Ct));
    }

    [Fact]
    public async Task Search_shows_stock_status_from_the_stock_store()
    {
        var store = new FakeStore();
        store.Add(Catalogue.Cap, 100);
        store.Add(Catalogue.SignedPhoto, 0);
        var service = new CatalogueService(store, store, new PassThroughTokens());

        var page = await service.SearchAsync(new ProductSearchRequest(null, null, null, null, null), Ct);

        Assert.Equal(StockStatus.InStock, page.Items.Single(i => i.Product.Id == "csk-cap").Status);
        Assert.Equal(StockStatus.OutOfStock, page.Items.Single(i => i.Product.Id == "kkr-signed-photo").Status);
    }
}

public class CatalogueFranchiseTests
{
    [Fact]
    public async Task Franchises_are_the_distinct_codes_of_the_products_in_code_order()
    {
        var store = new FakeStore();
        store.Add(Catalogue.SignedPhoto, 2);
        store.Add(Catalogue.Cap, 100);
        store.Add(Catalogue.Jersey, 50);
        var service = new CatalogueService(store, store, new PassThroughTokens());

        Assert.Equal(["csk", "kkr", "mi"], await service.ListFranchisesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_catalogue_has_no_franchises()
    {
        var store = new FakeStore();
        var service = new CatalogueService(store, store, new PassThroughTokens());

        Assert.Empty(await service.ListFranchisesAsync(CancellationToken.None));
    }
}

public class ProductCreationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static NewProductRequest Valid() =>
        new("csk", "csk-cap", "cap", "  CSK Cap  ", " Official cap. ", 49900, LimitedEdition: false, MaxPerCustomer: null, InitialStock: 100);

    private static (ProductAdminService Service, FakeAdminStore Admin) Create()
    {
        var store = new FakeStore();
        var admin = new FakeAdminStore();
        return (new ProductAdminService(store, store, admin, new PassThroughTokens(), new FakeClock(DateTimeOffset.UnixEpoch)), admin);
    }

    [Fact]
    public async Task A_valid_product_is_created_trimmed_with_its_opening_stock_and_actor()
    {
        var (service, admin) = Create();

        var created = await service.CreateAsync(Valid(), "admin-1", Ct);

        var (product, stock, actor) = Assert.Single(admin.Created);
        Assert.Equal(new Product("csk-cap", "csk", "CSK Cap", "cap", "Official cap.", 49900, false, null), product);
        Assert.Equal((100, "admin-1"), (stock, actor));
        Assert.Equal((100, StockStatus.InStock, "etag-1"), (created.Available, created.Status, created.ETag));
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once_and_nothing_is_written()
    {
        var (service, admin) = Create();
        var request = new NewProductRequest("CSK", "csk-cap", "hat", " ", null, 0, LimitedEdition: true, MaxPerCustomer: null, InitialStock: -1);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => service.CreateAsync(request, "admin-1", Ct));

        Assert.Equal(["franchiseId", "initialStock", "maxPerCustomer", "name", "pricePaise", "type"], error.Errors.Keys.Order().ToArray());
        Assert.Empty(admin.Created);
    }

    [Theory]
    [InlineData("cap")]
    [InlineData("mi-cap")]
    [InlineData("csk_cap")]
    [InlineData("csk-Cap")]
    [InlineData("csk-cap/1")]
    public async Task The_id_must_be_a_slug_starting_with_the_franchise_code(string id)
    {
        var (service, _) = Create();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => service.CreateAsync(Valid() with { Id = id }, "admin-1", Ct));

        Assert.Equal(["id"], error.Errors.Keys.ToArray());
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, 0)]
    [InlineData(true, 11)]
    [InlineData(false, 1)]
    public async Task Max_per_customer_is_required_for_and_only_for_limited_editions(bool limitedEdition, int? maxPerCustomer)
    {
        var (service, _) = Create();

        var error = await Assert.ThrowsAsync<ValidationFailedException>(
            () => service.CreateAsync(Valid() with { LimitedEdition = limitedEdition, MaxPerCustomer = maxPerCustomer }, "admin-1", Ct));

        Assert.Equal(["maxPerCustomer"], error.Errors.Keys.ToArray());
    }

    [Fact]
    public async Task A_limited_edition_keeps_its_maximum_per_customer()
    {
        var (service, admin) = Create();

        await service.CreateAsync(Valid() with { LimitedEdition = true, MaxPerCustomer = 1 }, "admin-1", Ct);

        Assert.Equal(1, Assert.Single(admin.Created).Product.MaxPerCustomer);
    }
}
