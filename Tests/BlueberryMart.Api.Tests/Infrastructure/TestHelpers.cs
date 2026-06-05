using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests.Infrastructure;

public static class TestHelpers
{
    /// <summary>Force an order's status directly in the DB (e.g. to 'confirmed'/'completed' for tests).</summary>
    public static async Task SetOrderStatusAsync(BlueberryMartApiFactory factory, Guid orderId, string status)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var order = await ctx.Orders.FirstAsync(o => o.Id == orderId);
        order.Status = status;
        await ctx.SaveChangesAsync();
    }

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
