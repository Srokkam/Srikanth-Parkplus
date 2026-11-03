using System.Net;
using IplFranchise.Application;

namespace IplFranchise.IntegrationTests;

/// <summary>Creating products through the admin API. A separate class, so these products live in their own database.</summary>
public class CatalogueAdminTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static object NewProduct(string franchiseId, string id, int initialStock = 25) => new
    {
        franchiseId,
        id,
        type = "cap",
        name = "DC Cap",
        description = "Official Delhi Capitals cap.",
        pricePaise = 54900L,
        limitedEdition = false,
        maxPerCustomer = (int?)null,
        initialStock,
    };

    [Fact]
    public async Task A_created_product_is_searchable_and_its_franchise_appears()
    {
        var shop = api.Start();
        var admin = await shop.AdminAsync();

        var created = await shop.PostAsync(admin, "/api/v1/admin/products", NewProduct("dc", "dc-cap"));

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal("/api/v1/products/dc/dc-cap", created.Headers.Location?.OriginalString);
        Assert.NotNull(created.ETag);
        Assert.Equal(25, created.Json["available"]!.GetValue<int>());
        Assert.Equal("in-stock", created.Json["stockStatus"]!.GetValue<string>());

        var found = (await shop.GetAsync(null, "/api/v1/products?franchise=dc&q=cap")).Json["items"]!.AsArray();
        Assert.Equal("DC Cap", Assert.Single(found)!["name"]!.GetValue<string>());

        var franchises = (await shop.GetAsync(null, "/api/v1/franchises")).Json.AsArray().Select(f => f!["code"]!.GetValue<string>());
        Assert.Contains("dc", franchises);

        Assert.Equal(25, await shop.StockAsync("dc", "dc-cap"));
    }

    [Fact]
    public async Task Creating_the_same_product_again_is_a_conflict_and_changes_nothing()
    {
        var shop = api.Start();
        var admin = await shop.AdminAsync();
        await shop.PostAsync(admin, "/api/v1/admin/products", NewProduct("dc", "dc-flag", initialStock: 10));

        var again = await shop.PostAsync(admin, "/api/v1/admin/products", NewProduct("dc", "dc-flag", initialStock: 99));

        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Equal(ProblemCodes.ProductExists, again.ProblemCode);
        Assert.Equal(10, await shop.StockAsync("dc", "dc-flag"));
    }

    [Fact]
    public async Task Only_admins_can_create_and_every_invalid_field_is_reported()
    {
        var shop = api.Start();
        var customer = await shop.SignInAsync(shop.NewUser());
        var admin = await shop.AdminAsync();

        var asCustomer = await shop.PostAsync(customer, "/api/v1/admin/products", NewProduct("dc", "dc-jersey"));
        var invalid = await shop.PostAsync(admin, "/api/v1/admin/products", new
        {
            franchiseId = "dc",
            id = "cap",
            type = "hat",
            name = "",
            pricePaise = 0L,
            limitedEdition = true,
            initialStock = -1,
        });

        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.Status);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        Assert.Equal(ProblemCodes.ValidationFailed, invalid.ProblemCode);
        Assert.Equal(
            ["id", "initialStock", "maxPerCustomer", "name", "pricePaise", "type"],
            invalid.Json["errors"]!.AsObject().Select(e => e.Key).Order());
    }
}
