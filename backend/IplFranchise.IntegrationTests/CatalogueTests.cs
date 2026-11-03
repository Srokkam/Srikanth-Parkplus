using System.Net;
using IplFranchise.Application;

namespace IplFranchise.IntegrationTests;

/// <summary>
/// Read-only checks on the opening catalogue added by migration 0003. Its own class, so no other test adds products to
/// this database and the counts stay exact.
/// </summary>
public class CatalogueTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Paging_returns_capped_pages_in_a_stable_name_order_and_visits_every_product_once(int pageSize)
    {
        var shop = api.Start();

        var names = new List<string>();
        string? token = null;
        int? total = null;
        do
        {
            var url = $"/api/v1/products?pageSize={pageSize}" + (token is null ? string.Empty : $"&continuationToken={Uri.EscapeDataString(token)}");
            var page = (await shop.GetAsync(null, url)).Json;
            var items = page["items"]!.AsArray();

            Assert.InRange(items.Count, 1, pageSize);
            total ??= page["totalCount"]?.GetValue<int>();
            names.AddRange(items.Select(i => i!["name"]!.GetValue<string>()));
            token = page["continuationToken"]?.GetValue<string>();
        }
        while (token is not null);

        Assert.Equal(4, total);
        Assert.Equal(["CSK Cap", "KKR Signed Photo", "MI Jersey", "RCB Flag"], names);
    }

    [Fact]
    public async Task Page_size_is_capped_at_50()
    {
        var shop = api.Start();

        var tooBig = await shop.GetAsync(null, "/api/v1/products?pageSize=51");

        Assert.Equal(HttpStatusCode.BadRequest, tooBig.Status);
        Assert.Equal(ProblemCodes.ValidationFailed, tooBig.ProblemCode);
        Assert.NotNull(tooBig.Json["errors"]!["pageSize"]);
    }

    [Fact]
    public async Task Name_search_is_case_insensitive_and_partial_and_results_show_price_and_stock_status()
    {
        var shop = api.Start();

        var found = (await shop.GetAsync(null, "/api/v1/products?q=JeRs")).Json["items"]!.AsArray();

        var jersey = Assert.Single(found)!;
        Assert.Equal("MI Jersey", jersey["name"]!.GetValue<string>());
        Assert.Equal(199900, jersey["pricePaise"]!.GetValue<long>());
        Assert.Equal("in-stock", jersey["stockStatus"]!.GetValue<string>());
    }

    [Fact]
    public async Task Filters_combine_and_a_tampered_or_reused_token_is_refused()
    {
        var shop = api.Start();

        var caps = (await shop.GetAsync(null, "/api/v1/products?type=cap&franchise=csk")).Json["items"]!.AsArray();
        Assert.Equal("csk-cap", Assert.Single(caps)!["id"]!.GetValue<string>());
        Assert.Empty((await shop.GetAsync(null, "/api/v1/products?type=cap&franchise=mi")).Json["items"]!.AsArray());

        var first = (await shop.GetAsync(null, "/api/v1/products?pageSize=2")).Json;
        var token = first["continuationToken"]!.GetValue<string>();
        var forged = await shop.GetAsync(null, $"/api/v1/products?pageSize=2&continuationToken={Uri.EscapeDataString(token[..^2] + "xx")}");
        var otherFilter = await shop.GetAsync(null, $"/api/v1/products?pageSize=2&type=cap&continuationToken={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.BadRequest, forged.Status);
        Assert.Equal(ProblemCodes.InvalidContinuationToken, forged.ProblemCode);
        Assert.Equal(ProblemCodes.InvalidContinuationToken, otherFilter.ProblemCode);
    }

    [Fact]
    public async Task Franchises_are_the_codes_used_by_the_products_in_the_catalogue()
    {
        var shop = api.Start();

        var franchises = (await shop.GetAsync(null, "/api/v1/franchises")).Json.AsArray().Select(f => f!["code"]!.GetValue<string>());

        Assert.Equal(["csk", "kkr", "mi", "rcb"], franchises);
    }

    [Fact]
    public async Task The_opening_stock_is_in_the_audit_trail()
    {
        var shop = api.Start();
        var admin = await shop.AdminAsync();

        var audit = (await shop.GetAsync(admin, "/api/v1/admin/products/kkr/kkr-signed-photo/audit?field=stock")).Json["items"]!.AsArray();

        var opening = Assert.Single(audit)!;
        Assert.Equal(0, opening["oldValue"]!.GetValue<long>());
        Assert.Equal(2, opening["newValue"]!.GetValue<long>());
        Assert.Equal("initial stock", opening["reason"]!.GetValue<string>());
        Assert.Equal("migration:0003-initial-catalogue", opening["actor"]!.GetValue<string>());
    }
}
