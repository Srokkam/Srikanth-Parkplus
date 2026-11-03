using System.Net;
using IplFranchise.Application;
using IplFranchise.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace IplFranchise.IntegrationTests;

/// <summary>Shopping scenarios against the opening catalogue from migration 0003. Each test sets the stock it relies on.</summary>
public class ShopFlowTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static string NewOrderId() => Guid.NewGuid().ToString();

    [Fact]
    public async Task Only_one_of_two_customers_fighting_for_the_last_unit_gets_it()
    {
        var shop = api.Start();
        await shop.SetStockAsync("rcb", "rcb-flag", 1);

        var asha = await shop.SignInAsync(shop.NewUser());
        var ravi = await shop.SignInAsync(shop.NewUser());
        foreach (var customer in new[] { asha, ravi })
        {
            Assert.Equal(HttpStatusCode.OK, (await shop.AddToCartAsync(customer, "rcb", "rcb-flag", 1)).Status);
        }

        var results = await Task.WhenAll(
            shop.PutAsync(asha, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = 29900 }),
            shop.PutAsync(ravi, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = 29900 }));

        Assert.Single(results, r => r.Status == HttpStatusCode.Created);
        var loser = Assert.Single(results, r => r.Status == HttpStatusCode.Conflict);
        Assert.Equal(ProblemCodes.OutOfStock, loser.ProblemCode);
        Assert.Equal(0, await shop.StockAsync("rcb", "rcb-flag"));
    }

    [Fact]
    public async Task Repeating_a_checkout_never_creates_a_second_order_or_deducts_twice()
    {
        var shop = api.Start();
        await shop.SetStockAsync("csk", "csk-cap", 10);
        var customer = await shop.SignInAsync(shop.NewUser());
        await shop.AddToCartAsync(customer, "csk", "csk-cap", 2);
        var orderId = NewOrderId();

        var first = await shop.PutAsync(customer, $"/api/v1/orders/{orderId}", new { acceptedTotalPaise = 99800 });
        var again = await shop.PutAsync(customer, $"/api/v1/orders/{orderId}", new { acceptedTotalPaise = 99800 });

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(first.Json["id"]!.GetValue<string>(), again.Json["id"]!.GetValue<string>());
        Assert.Equal(8, await shop.StockAsync("csk", "csk-cap"));

        var history = (await shop.GetAsync(customer, "/api/v1/orders")).Json["items"]!.AsArray();
        Assert.Single(history);
    }

    [Fact]
    public async Task A_burst_of_identical_checkouts_places_one_order()
    {
        var shop = api.Start();
        await shop.SetStockAsync("mi", "mi-jersey", 10);
        var customer = await shop.SignInAsync(shop.NewUser());
        await shop.AddToCartAsync(customer, "mi", "mi-jersey", 1);
        var orderId = NewOrderId();

        var replies = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => shop.PutAsync(customer, $"/api/v1/orders/{orderId}", new { acceptedTotalPaise = 199900 })));

        Assert.Single(replies, r => r.Status == HttpStatusCode.Created);
        Assert.All(replies.Where(r => r.Status != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(9, await shop.StockAsync("mi", "mi-jersey"));
    }

    [Fact]
    public async Task A_checkout_interrupted_after_the_stock_moved_is_completed_by_the_sweeper()
    {
        var shop = api.Start();
        await shop.SetStockAsync("rcb", "rcb-flag", 10);
        var userId = shop.NewUser();
        var customer = await shop.SignInAsync(userId);
        await shop.AddToCartAsync(customer, "rcb", "rcb-flag", 2);

        // Step one only: the process "dies" before the order record is written.
        var orderId = Guid.NewGuid().ToString("N");
        var order = new Order(orderId, userId, DateTimeOffset.UtcNow.AddMinutes(-10), [new OrderLine("rcb-flag", "rcb", "RCB Flag", 29900, 2)], 0);
        var outcome = await api.Service<ICheckoutStore>().DeductAsync(order, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(DeductOutcome.Deducted, outcome);
        Assert.Equal(HttpStatusCode.NotFound, (await shop.GetAsync(customer, $"/api/v1/orders/{orderId}")).Status);

        await using (var scope = api.Service<IServiceScopeFactory>().CreateAsyncScope())
        {
            var swept = await scope.ServiceProvider.GetRequiredService<CheckoutRecovery>().CompleteStaleAsync(TimeSpan.Zero, CancellationToken.None);
            Assert.True(swept >= 1);
        }

        var recovered = await shop.GetAsync(customer, $"/api/v1/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, recovered.Status);
        Assert.Equal(59800, recovered.Json["totalPaise"]!.GetValue<long>());
        Assert.Equal(8, await shop.StockAsync("rcb", "rcb-flag"));
    }

    [Fact]
    public async Task A_price_change_must_be_accepted_before_checkout()
    {
        var shop = api.Start();
        // Its own product, so the price change can't affect the totals other tests expect.
        await shop.CreateProductAsync("csk", "csk-price-test", "CSK Price Test", 100000, stock: 10);
        var customer = await shop.SignInAsync(shop.NewUser());
        var admin = await shop.AdminAsync();
        await shop.AddToCartAsync(customer, "csk", "csk-price-test", 1);

        var view = await shop.GetAsync(admin, "/api/v1/admin/products/csk/csk-price-test");
        var oldPrice = view.Json["pricePaise"]!.GetValue<long>();
        var change = await shop.PutAsync(
            admin, "/api/v1/admin/products/csk/csk-price-test/price",
            new { pricePaise = oldPrice + 10000, reason = "season pricing" }, ifMatch: view.Json["priceETag"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, change.Status);

        var cart = (await shop.GetAsync(customer, "/api/v1/cart")).Json;
        Assert.True(cart["requiresAcceptance"]!.GetValue<bool>());

        var stale = await shop.PutAsync(customer, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = oldPrice });
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(ProblemCodes.PriceChanged, stale.ProblemCode);
        Assert.Equal(oldPrice + 10000, stale.Json["currentTotalPaise"]!.GetValue<long>());

        await shop.PostAsync(customer, "/api/v1/cart/accept-changes", new { expectedTotalPaise = oldPrice + 10000 }, Guid.NewGuid().ToString());
        var placed = await shop.PutAsync(customer, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = oldPrice + 10000 });
        Assert.Equal(HttpStatusCode.Created, placed.Status);

        var audit = (await shop.GetAsync(admin, "/api/v1/admin/products/csk/csk-price-test/audit?field=price")).Json["items"]!.AsArray();
        var latest = audit[0]!;
        Assert.Equal(oldPrice, latest["oldValue"]!.GetValue<long>());
        Assert.Equal(oldPrice + 10000, latest["newValue"]!.GetValue<long>());
    }

    [Fact]
    public async Task Admin_edits_need_the_version_that_was_read()
    {
        var shop = api.Start();
        var admin = await shop.AdminAsync();
        var url = "/api/v1/admin/products/mi/mi-jersey";

        var read = await shop.GetAsync(admin, url);
        var stockETag = read.Json["stockETag"]!.GetValue<string>();

        var missing = await shop.PutAsync(admin, $"{url}/stock", new { available = 5, reason = "recount" });
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.Status);

        var first = await shop.PutAsync(admin, $"{url}/stock", new { available = 5, reason = "recount" }, ifMatch: stockETag);
        Assert.Equal(HttpStatusCode.OK, first.Status);

        var stale = await shop.PutAsync(admin, $"{url}/stock", new { available = 7, reason = "recount" }, ifMatch: stockETag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.Status);
        Assert.Equal(ProblemCodes.PreconditionFailed, stale.ProblemCode);
        Assert.Equal(5, await shop.StockAsync("mi", "mi-jersey"));
    }

    [Fact]
    public async Task Cart_changes_honour_the_idempotency_key()
    {
        var shop = api.Start();
        await shop.SetStockAsync("csk", "csk-cap", 50);
        var customer = await shop.SignInAsync(shop.NewUser());
        var key = Guid.NewGuid().ToString();

        await shop.AddToCartAsync(customer, "csk", "csk-cap", 2, key);
        var repeat = await shop.AddToCartAsync(customer, "csk", "csk-cap", 2, key);
        var cart = (await shop.GetAsync(customer, "/api/v1/cart")).Json;

        Assert.Equal(HttpStatusCode.OK, repeat.Status);
        Assert.Equal(2, cart["lines"]![0]!["quantity"]!.GetValue<int>());

        var reused = await shop.AddToCartAsync(customer, "csk", "csk-cap", 5, key);
        Assert.Equal(ProblemCodes.IdempotencyKeyReused, reused.ProblemCode);

        var noKey = await shop.PostAsync(customer, "/api/v1/cart/items", new { franchiseId = "csk", productId = "csk-cap", quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, noKey.Status);
        Assert.Equal(ProblemCodes.IdempotencyKeyRequired, noKey.ProblemCode);
    }

    [Fact]
    public async Task A_limited_edition_can_only_be_bought_once_per_customer()
    {
        var shop = api.Start();
        await shop.SetStockAsync("kkr", "kkr-signed-photo", 5);
        var customer = await shop.SignInAsync(shop.NewUser());

        await shop.AddToCartAsync(customer, "kkr", "kkr-signed-photo", 1);
        var first = await shop.PutAsync(customer, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = 499900 });
        Assert.Equal(HttpStatusCode.Created, first.Status);

        await shop.AddToCartAsync(customer, "kkr", "kkr-signed-photo", 1);
        var second = await shop.PutAsync(customer, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = 499900 });

        Assert.Equal(HttpStatusCode.Conflict, second.Status);
        Assert.Equal(ProblemCodes.QuantityLimitExceeded, second.ProblemCode);
        Assert.Equal(4, await shop.StockAsync("kkr", "kkr-signed-photo"));
    }

    [Fact]
    public async Task Quantities_of_1_and_10_are_accepted_and_11_is_refused()
    {
        var shop = api.Start();
        await shop.SetStockAsync("csk", "csk-cap", 100);
        var customer = await shop.SignInAsync(shop.NewUser());

        Assert.Equal(HttpStatusCode.OK, (await shop.AddToCartAsync(customer, "csk", "csk-cap", 1)).Status);
        var ten = await shop.PutAsync(customer, "/api/v1/cart/items/csk-cap", new { quantity = 10 }, Guid.NewGuid().ToString());
        Assert.Equal(10, ten.Json["lines"]![0]!["quantity"]!.GetValue<int>());

        var eleven = await shop.PutAsync(customer, "/api/v1/cart/items/csk-cap", new { quantity = 11 }, Guid.NewGuid().ToString());
        var zero = await shop.AddToCartAsync(customer, "csk", "csk-cap", 0);
        var oneMore = await shop.AddToCartAsync(customer, "csk", "csk-cap", 1);

        Assert.Equal(HttpStatusCode.BadRequest, eleven.Status);
        Assert.Equal(ProblemCodes.ValidationFailed, eleven.ProblemCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.Status);
        Assert.Equal(HttpStatusCode.Conflict, oneMore.Status);
        Assert.Equal(ProblemCodes.QuantityLimitExceeded, oneMore.ProblemCode);
    }

    [Fact]
    public async Task With_exactly_one_in_stock_one_can_be_added_but_not_two()
    {
        var shop = api.Start();
        await shop.SetStockAsync("rcb", "rcb-flag", 1);
        var customer = await shop.SignInAsync(shop.NewUser());

        var two = await shop.AddToCartAsync(customer, "rcb", "rcb-flag", 2);
        var one = await shop.AddToCartAsync(customer, "rcb", "rcb-flag", 1);

        Assert.Equal(HttpStatusCode.Conflict, two.Status);
        Assert.Equal(ProblemCodes.InsufficientStock, two.ProblemCode);
        Assert.Equal(1, two.Json["available"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.OK, one.Status);

        var placed = await shop.PutAsync(customer, $"/api/v1/orders/{NewOrderId()}", new { acceptedTotalPaise = 29900 });
        Assert.Equal(HttpStatusCode.Created, placed.Status);
        Assert.Equal(0, await shop.StockAsync("rcb", "rcb-flag"));
    }

    [Fact]
    public async Task Protected_endpoints_answer_with_problem_details()
    {
        var shop = api.Start();

        var anonymous = await shop.GetAsync(null, "/api/v1/cart");
        var customer = await shop.SignInAsync(shop.NewUser());
        var notAdmin = await shop.GetAsync(customer, "/api/v1/admin/products/csk/csk-cap");
        var missing = await shop.GetAsync(null, "/api/v1/products/csk/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.Status);
        Assert.Equal(HttpStatusCode.Forbidden, notAdmin.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal(ProblemCodes.NotFound, missing.ProblemCode);
        Assert.StartsWith("/problems/", missing.Json["type"]!.GetValue<string>());
    }
}
