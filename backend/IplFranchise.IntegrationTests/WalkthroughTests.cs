using System.Net;
using System.Text.Json.Nodes;
using IplFranchise.Application;

namespace IplFranchise.IntegrationTests;

/// <summary>
/// The assessment's Example Walkthrough, step by step, against the real API and the opening catalogue that migration
/// 0003 creates (MI Jersey 50, CSK Cap 100, RCB Flag 200, KKR Signed Photo 2). Its own class, so its own database.
/// </summary>
public class WalkthroughTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_example_walkthrough_behaves_as_described()
    {
        var shop = api.Start();
        var asha = await shop.SignInAsync("asha");
        var ravi = await shop.SignInAsync("ravi");
        var admin = await shop.AdminAsync();

        // Open the product list with a page size of 2: two of the four products, the total count and a next page.
        var first = (await shop.GetAsync(null, "/api/v1/products?pageSize=2")).Json;
        Assert.Equal(2, first["items"]!.AsArray().Count);
        Assert.Equal(4, first["totalCount"]!.GetValue<int>());
        var next = first["continuationToken"]!.GetValue<string>();
        var second = (await shop.GetAsync(null, $"/api/v1/products?pageSize=2&continuationToken={Uri.EscapeDataString(next)}")).Json;
        Assert.Equal(2, second["items"]!.AsArray().Count);
        Assert.Null(second["continuationToken"]);

        var prices = first["items"]!.AsArray().Concat(second["items"]!.AsArray())
            .ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!["pricePaise"]!.GetValue<long>());
        Assert.Equal(199900, prices["MI Jersey"]);
        Assert.Equal(49900, prices["CSK Cap"]);
        Assert.Equal(499900, prices["KKR Signed Photo"]);

        // Search with type Cap and franchise Chennai Super Kings: one result, CSK Cap, ₹499.00, in stock.
        var caps = (await shop.GetAsync(null, "/api/v1/products?type=cap&franchise=csk")).Json["items"]!.AsArray();
        var cap = Assert.Single(caps)!;
        Assert.Equal(("CSK Cap", 49900L, "in-stock"), (cap["name"]!.GetValue<string>(), cap["pricePaise"]!.GetValue<long>(), cap["stockStatus"]!.GetValue<string>()));

        // Asha adds 2 MI Jerseys: 2 × ₹1,999.00 = ₹3,998.00.
        var cart = (await shop.AddToCartAsync(asha, "mi", "mi-jersey", 2)).Json;
        Assert.Equal(399800, Line(cart, "mi-jersey")["lineTotalPaise"]!.GetValue<long>());
        Assert.Equal(399800, cart["totalPaise"]!.GetValue<long>());

        // Asha adds 3 CSK Caps: line ₹1,497.00, cart ₹5,495.00.
        cart = (await shop.AddToCartAsync(asha, "csk", "csk-cap", 3)).Json;
        Assert.Equal(149700, Line(cart, "csk-cap")["lineTotalPaise"]!.GetValue<long>());
        Assert.Equal(549500, cart["totalPaise"]!.GetValue<long>());

        // Asha adds 1 KKR Signed Photo and the request is sent twice: added once, cart ₹10,494.00.
        var photoRequest = Guid.NewGuid().ToString();
        var once = (await shop.AddToCartAsync(asha, "kkr", "kkr-signed-photo", 1, photoRequest)).Json;
        var twice = (await shop.AddToCartAsync(asha, "kkr", "kkr-signed-photo", 1, photoRequest)).Json;
        Assert.Equal(1049400, once["totalPaise"]!.GetValue<long>());
        Assert.Equal(1049400, twice["totalPaise"]!.GetValue<long>());
        Assert.Equal(1, Line(twice, "kkr-signed-photo")["quantity"]!.GetValue<int>());

        // The walkthrough starts the photo at 2 but ends at 0 after one order, so one is already sold: another fan buys it.
        var kiran = await shop.SignInAsync("kiran");
        await shop.AddToCartAsync(kiran, "kkr", "kkr-signed-photo", 1);
        Assert.Equal(HttpStatusCode.Created, (await shop.PutAsync(kiran, $"/api/v1/orders/{Guid.NewGuid()}", new { acceptedTotalPaise = 499900 })).Status);
        Assert.Equal(1, await shop.StockAsync("kkr", "kkr-signed-photo"));

        // Ravi, with only the last photo in his cart, and Asha check out at the same moment: only one order gets it.
        Assert.Equal(HttpStatusCode.OK, (await shop.AddToCartAsync(ravi, "kkr", "kkr-signed-photo", 1)).Status);
        var ashaOrderId = Guid.NewGuid().ToString();
        var replies = await Task.WhenAll(
            shop.PutAsync(asha, $"/api/v1/orders/{ashaOrderId}", new { acceptedTotalPaise = 1049400 }),
            shop.PutAsync(ravi, $"/api/v1/orders/{Guid.NewGuid()}", new { acceptedTotalPaise = 499900 }));
        var (ashaReply, raviReply) = (replies[0], replies[1]);
        Assert.Single(replies, r => r.Status == HttpStatusCode.Created);

        var ashaWon = ashaReply.Status == HttpStatusCode.Created;
        var (winner, loser, loserClient) = ashaWon ? (ashaReply, raviReply, ravi) : (raviReply, ashaReply, asha);

        // The loser is told the photo is out of stock; no order is created and the cart is kept.
        Assert.Equal(HttpStatusCode.Conflict, loser.Status);
        Assert.Equal(ProblemCodes.OutOfStock, loser.ProblemCode);
        Assert.Contains(loser.Json["failedLines"]!.AsArray(), l => l!["productId"]!.GetValue<string>() == "kkr-signed-photo");
        Assert.Contains((await shop.GetAsync(loserClient, "/api/v1/cart")).Json["lines"]!.AsArray(), l => l!["productId"]!.GetValue<string>() == "kkr-signed-photo");
        Assert.Empty((await shop.GetAsync(loserClient, "/api/v1/orders")).Json["items"]!.AsArray());
        Assert.Equal(0, await shop.StockAsync("kkr", "kkr-signed-photo"));

        long ashaTotal;
        Reply ashaOrder;
        if (ashaWon)
        {
            // Asha first: confirmed for ₹10,494.00; stock MI Jersey 48, CSK Cap 97, KKR Signed Photo 0.
            ashaOrder = winner;
            ashaTotal = 1049400;
        }
        else
        {
            // Ravi first (the mirror image): nothing was deducted for Asha's rejected order. She drops the photo and buys the rest.
            Assert.Equal(499900, winner.Json["totalPaise"]!.GetValue<long>());
            Assert.Equal(50, await shop.StockAsync("mi", "mi-jersey"));
            Assert.Equal(100, await shop.StockAsync("csk", "csk-cap"));
            await shop.DeleteAsync(asha, "/api/v1/cart/items/kkr-signed-photo", Guid.NewGuid().ToString());
            ashaOrderId = Guid.NewGuid().ToString();
            ashaOrder = await shop.PutAsync(asha, $"/api/v1/orders/{ashaOrderId}", new { acceptedTotalPaise = 549500 });
            Assert.Equal(HttpStatusCode.Created, ashaOrder.Status);
            ashaTotal = 549500;
        }

        Assert.Equal(ashaTotal, ashaOrder.Json["totalPaise"]!.GetValue<long>());
        Assert.Equal(48, await shop.StockAsync("mi", "mi-jersey"));
        Assert.Equal(97, await shop.StockAsync("csk", "csk-cap"));
        Assert.Empty((await shop.GetAsync(asha, "/api/v1/cart")).Json["lines"]!.AsArray());

        // Asha's checkout is retried with the same order id: the same order comes back, no second order, stock unchanged.
        var retry = await shop.PutAsync(asha, $"/api/v1/orders/{ashaOrderId}", new { acceptedTotalPaise = ashaTotal });
        Assert.Equal(HttpStatusCode.OK, retry.Status);
        Assert.Equal(ashaOrder.Json["id"]!.GetValue<string>(), retry.Json["id"]!.GetValue<string>());
        Assert.Equal(48, await shop.StockAsync("mi", "mi-jersey"));
        Assert.Equal(97, await shop.StockAsync("csk", "csk-cap"));

        // An admin raises the CSK Cap price to ₹549.00 (optimistic concurrency: the version read goes in If-Match).
        var adminView = (await shop.GetAsync(admin, "/api/v1/admin/products/csk/csk-cap")).Json;
        var raised = await shop.PutAsync(
            admin, "/api/v1/admin/products/csk/csk-cap/price", new { pricePaise = 54900, reason = "price review" },
            ifMatch: adminView["priceETag"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, raised.Status);

        // Asha's order history: one order at the old prices (caps at ₹499.00 each); the product page shows ₹549.00.
        var history = (await shop.GetAsync(asha, "/api/v1/orders")).Json["items"]!.AsArray();
        var order = Assert.Single(history)!;
        Assert.Equal(ashaTotal, order["totalPaise"]!.GetValue<long>());
        Assert.Equal(49900, Line(order, "csk-cap")["unitPricePaise"]!.GetValue<long>());
        Assert.Equal(TimeSpan.Zero, order["createdUtc"]!.GetValue<DateTimeOffset>().Offset);
        Assert.Equal(54900, (await shop.GetAsync(null, "/api/v1/products/csk/csk-cap")).Json["pricePaise"]!.GetValue<long>());
    }

    [Fact]
    public async Task Orders_keep_every_v1_field_and_add_item_count_backwards_compatibly()
    {
        var shop = api.Start();
        var customer = await shop.SignInAsync(shop.NewUser());
        // Only RCB Flags: this class shares its database with the walkthrough, which never looks at that stock.
        await shop.AddToCartAsync(customer, "rcb", "rcb-flag", 3);

        var placed = (await shop.PutAsync(customer, $"/api/v1/orders/{Guid.NewGuid()}", new { acceptedTotalPaise = 89700 })).Json.AsObject();

        // Every field v1 clients were built against is still there, unchanged...
        string[] v1 = ["id", "userId", "createdUtc", "lines", "shippingPaise", "subtotalPaise", "totalPaise"];
        Assert.All(v1, field => Assert.True(placed.ContainsKey(field), $"v1 field '{field}' is missing"));
        Assert.All(new[] { "productId", "franchiseId", "name", "unitPricePaise", "quantity", "lineTotalPaise" },
            field => Assert.True(placed["lines"]![0]!.AsObject().ContainsKey(field), $"v1 line field '{field}' is missing"));

        // ...and the one addition is optional for clients: they can ignore it and still sum the lines.
        Assert.Equal(3, placed["itemCount"]!.GetValue<int>());
        Assert.Equal(3, placed["lines"]!.AsArray().Sum(l => l!["quantity"]!.GetValue<int>()));
    }

    private static JsonNode Line(JsonNode cartOrOrder, string productId) =>
        cartOrOrder["lines"]!.AsArray().Single(l => l!["productId"]!.GetValue<string>() == productId)!;
}
