using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IplFranchise.IntegrationTests;

public sealed record Reply(HttpStatusCode Status, JsonNode? Body, HttpResponseHeaders Headers)
{
    public string? ETag => Headers.ETag?.Tag;

    public string? ProblemCode => Body?["code"]?.GetValue<string>();

    public JsonNode Json => Body ?? throw new InvalidOperationException($"No body ({Status}).");
}

/// <summary>A tiny typed-less client: tests read the JSON the way a front end would.</summary>
public sealed class ShopClient(WebApplicationFactory<Program> factory)
{
    private readonly HttpClient anonymous = factory.CreateClient();

    public string NewUser() => $"user-{Guid.NewGuid():N}"[..20];

    public async Task<HttpClient> SignInAsync(string userId, bool admin = false)
    {
        var response = await anonymous.PostAsJsonAsync("/api/dev/token", new { userId, admin });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task<HttpClient> AdminAsync() => SignInAsync("admin-1", admin: true);

    public Task<Reply> GetAsync(HttpClient? client, string url) => SendAsync(client ?? anonymous, HttpMethod.Get, url);

    public Task<Reply> PostAsync(HttpClient client, string url, object? body, string? idempotencyKey = null) =>
        SendAsync(client, HttpMethod.Post, url, body, idempotencyKey);

    public Task<Reply> PutAsync(HttpClient client, string url, object? body, string? idempotencyKey = null, string? ifMatch = null) =>
        SendAsync(client, HttpMethod.Put, url, body, idempotencyKey, ifMatch);

    public Task<Reply> DeleteAsync(HttpClient client, string url, string? idempotencyKey = null) =>
        SendAsync(client, HttpMethod.Delete, url, null, idempotencyKey);

    public async Task<Reply> AddToCartAsync(HttpClient client, string franchise, string product, int quantity, string? key = null) =>
        await PostAsync(client, "/api/v1/cart/items", new { franchiseId = franchise, productId = product, quantity }, key ?? Guid.NewGuid().ToString());

    /// <summary>Sets the stock level through the admin API, the way an operator would.</summary>
    public async Task SetStockAsync(string franchise, string product, int available)
    {
        var admin = await AdminAsync();
        var current = await GetAsync(admin, $"/api/v1/admin/products/{franchise}/{product}");
        var reply = await PutAsync(
            admin, $"/api/v1/admin/products/{franchise}/{product}/stock",
            new { available, reason = "test setup" }, ifMatch: current.Json["stockETag"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, reply.Status);
    }

    /// <summary>Adds a product through the admin API, for tests that need one the opening catalogue doesn't have.</summary>
    public async Task CreateProductAsync(string franchise, string id, string name, long pricePaise, int stock, string type = "other")
    {
        var reply = await PostAsync(await AdminAsync(), "/api/v1/admin/products", new
        {
            franchiseId = franchise,
            id,
            type,
            name,
            description = $"{name} for tests.",
            pricePaise,
            limitedEdition = false,
            initialStock = stock,
        });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    public async Task<int> StockAsync(string franchise, string product)
    {
        var admin = await AdminAsync();
        return (await GetAsync(admin, $"/api/v1/admin/products/{franchise}/{product}")).Json["available"]!.GetValue<int>();
    }

    private static async Task<Reply> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body = null, string? idempotencyKey = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new Reply(response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response.Headers);
    }
}
