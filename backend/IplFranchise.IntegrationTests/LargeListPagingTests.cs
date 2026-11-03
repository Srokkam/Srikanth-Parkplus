using System.Net;

namespace IplFranchise.IntegrationTests;

/// <summary>Paging and ordering over lists much longer than one page. Its own class, so its own database.</summary>
public class LargeListPagingTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task A_long_product_list_pages_in_name_order_without_gaps_or_repeats()
    {
        var shop = api.Start();

        // 45 more products on top of the opening 4, with mixed case to prove the order ignores case.
        var created = Enumerable.Range(1, 45).Select(i => (Id: $"test-item-{i:D2}", Name: (i % 2 == 0 ? "test item " : "TEST Item ") + i.ToString("D2"))).ToList();
        foreach (var chunk in created.Chunk(9))
        {
            await Task.WhenAll(chunk.Select(p => shop.CreateProductAsync("test", p.Id, p.Name, 10000 + 100, stock: 5)));
        }

        var seen = new List<(string Name, string Id)>();
        string? token = null;
        var pages = 0;
        do
        {
            var url = "/api/v1/products?pageSize=7" + (token is null ? string.Empty : $"&continuationToken={Uri.EscapeDataString(token)}");
            var page = (await shop.GetAsync(null, url)).Json;
            var items = page["items"]!.AsArray();
            Assert.InRange(items.Count, 1, 7);
            if (pages == 0)
            {
                Assert.Equal(49, page["totalCount"]!.GetValue<int>());
            }

            seen.AddRange(items.Select(i => (i!["name"]!.GetValue<string>(), i["id"]!.GetValue<string>())));
            token = page["continuationToken"]?.GetValue<string>();
            pages++;
        }
        while (token is not null);

        Assert.Equal(7, pages);
        Assert.Equal(49, seen.Count);
        Assert.Equal(49, seen.Select(p => p.Id).Distinct().Count());
        Assert.Equal(seen.OrderBy(p => p.Name.ToLowerInvariant(), StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal), seen);
    }

    [Fact]
    public async Task A_long_order_history_pages_newest_first_and_filters_by_date_range()
    {
        var shop = api.Start();
        await shop.SetStockAsync("rcb", "rcb-flag", 100);
        var customer = await shop.SignInAsync(shop.NewUser());

        var placed = new List<(string Id, DateTimeOffset CreatedUtc)>();
        for (var i = 0; i < 14; i++)
        {
            await shop.AddToCartAsync(customer, "rcb", "rcb-flag", 1);
            var order = await shop.PutAsync(customer, $"/api/v1/orders/{Guid.NewGuid()}", new { acceptedTotalPaise = 29900 });
            Assert.Equal(HttpStatusCode.Created, order.Status);
            placed.Add((order.Json["id"]!.GetValue<string>(), order.Json["createdUtc"]!.GetValue<DateTimeOffset>()));
        }

        var all = await ReadAllAsync(shop, customer, string.Empty, pageSize: 4);

        Assert.Equal(placed.Select(o => o.Id).Reverse(), all.Select(o => o.Id));
        Assert.True(all.Zip(all.Skip(1)).All(pair => pair.First.CreatedUtc >= pair.Second.CreatedUtc));

        // 'from' is inclusive and 'to' exclusive: the 4th to the 9th order placed.
        var from = placed[3].CreatedUtc;
        var to = placed[9].CreatedUtc;
        var range = await ReadAllAsync(shop, customer, $"&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", pageSize: 4);

        Assert.Equal(placed.Skip(3).Take(6).Select(o => o.Id).Reverse(), range.Select(o => o.Id));
    }

    [Fact]
    public async Task An_inverted_date_range_is_a_validation_problem()
    {
        var shop = api.Start();
        var customer = await shop.SignInAsync(shop.NewUser());

        var reply = await shop.GetAsync(customer, "/api/v1/orders?from=2026-11-02T00:00:00Z&to=2026-11-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.NotNull(reply.Json["errors"]!["from"]);
    }

    private static async Task<List<(string Id, DateTimeOffset CreatedUtc)>> ReadAllAsync(ShopClient shop, HttpClient customer, string filter, int pageSize)
    {
        var orders = new List<(string Id, DateTimeOffset CreatedUtc)>();
        string? token = null;
        do
        {
            var url = $"/api/v1/orders?pageSize={pageSize}{filter}" + (token is null ? string.Empty : $"&continuationToken={Uri.EscapeDataString(token)}");
            var page = (await shop.GetAsync(customer, url)).Json;
            var items = page["items"]!.AsArray();
            Assert.InRange(items.Count, 0, pageSize);
            orders.AddRange(items.Select(o => (o!["id"]!.GetValue<string>(), o["createdUtc"]!.GetValue<DateTimeOffset>())));
            token = page["continuationToken"]?.GetValue<string>();
        }
        while (token is not null);

        return orders;
    }
}
