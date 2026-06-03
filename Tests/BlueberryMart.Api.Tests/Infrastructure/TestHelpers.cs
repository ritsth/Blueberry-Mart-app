using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BlueberryMart.Api.Tests.Infrastructure;

public static class TestHelpers
{
    public static async Task<string> GetCustomerTokenAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "customer1@blueberrymart.com",
            password = "customer1_password"
        });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    public static async Task<string> GetShareholderTokenAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "shareholder1@blueberrymart.com",
            password = "shareholder1_password"
        });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    public static async Task<Guid> PlaceOrderAsync(
        HttpClient client, string token, Guid branchId, Guid itemId, int quantity = 1)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId,
                orderType = "pickup",
                items     = new[] { new { itemId, quantity } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.SendAsync(req);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(json.GetProperty("id").GetString()!);
    }

    public static HttpRequestMessage WithBearer(this HttpRequestMessage req, string token)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }
}
