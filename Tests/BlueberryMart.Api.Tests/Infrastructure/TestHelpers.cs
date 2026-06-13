using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
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

    /// <summary>Marks an order paid: adds a completed payment + sets status 'confirmed'. Returns the order total.</summary>
    public static async Task<decimal> MarkOrderPaidAsync(BlueberryMartApiFactory factory, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var order = await ctx.Orders.FirstAsync(o => o.Id == orderId);
        order.Status = "confirmed";
        ctx.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TransactionUuid = $"test-{Guid.NewGuid()}",
            Amount = order.TotalAmount,
            Status = "completed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return order.TotalAmount;
    }

    /// <summary>Reads an inventory item's current stock quantity directly from the DB.</summary>
    public static async Task<int> GetStockAsync(BlueberryMartApiFactory factory, Guid itemId)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        return (await ctx.Inventory.FirstAsync(i => i.Id == itemId)).StockQuantity;
    }

    /// <summary>Reads an order's status, channel and owning user directly from the DB.
    /// <c>UserId</c> is null for an anonymous in-store walk-in sale.</summary>
    public static async Task<(string Status, string Channel, Guid? UserId)> GetOrderInfoAsync(
        BlueberryMartApiFactory factory, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var o = await ctx.Orders.FirstAsync(x => x.Id == orderId);
        return (o.Status, o.Channel, o.UserId);
    }

    /// <summary>True if the order has a completed payment.</summary>
    public static async Task<bool> OrderHasCompletedPaymentAsync(BlueberryMartApiFactory factory, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        return await ctx.Payments.AnyAsync(p => p.OrderId == orderId && p.Status == "completed");
    }

    /// <summary>Reads a user's current loyalty-point balance.</summary>
    public static async Task<int> GetLoyaltyPointsAsync(BlueberryMartApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        return (await ctx.Users.FirstAsync(u => u.Id == userId)).LoyaltyPoints;
    }

    /// <summary>Forces an inventory item's stock directly in the DB (e.g. to 0 to simulate sold-out).</summary>
    public static async Task SetStockAsync(BlueberryMartApiFactory factory, Guid itemId, int stock)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var item = await ctx.Inventory.FirstAsync(i => i.Id == itemId);
        item.StockQuantity = stock;
        await ctx.SaveChangesAsync();
    }

    /// <summary>Creates a fresh inventory item (default out of stock) so tests don't disturb seeded items.</summary>
    public static async Task<Guid> CreateInventoryItemAsync(
        BlueberryMartApiFactory factory, Guid branchId, string name, int stock = 0)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var item = new Inventory
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ItemName = name,
            StockQuantity = stock,
            Price = 100m
        };
        ctx.Inventory.Add(item);
        await ctx.SaveChangesAsync();
        return item.Id;
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

    public static async Task<string> GetAdminTokenAsync(HttpClient client)
        => await GetTokenAsync(client, BlueberryMartApiFactory.AdminEmail, BlueberryMartApiFactory.AdminPassword);

    public static async Task<string> GetTokenAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    /// <summary>Creates a throwaway user (default customer) so ban tests don't disturb seeded accounts.</summary>
    public static async Task<Guid> CreateUserAsync(
        BlueberryMartApiFactory factory, string email, string password, string role = "customer",
        Guid? branchId = null)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLower(),
            // Matches AuthController's SHA256-base64 hashing so the user can log in.
            PasswordHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password))),
            Role = role,
            BranchId = branchId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Demotes every admin except the given email — makes "last admin" assertions deterministic
    /// despite other tests creating admins in the shared DB.</summary>
    public static async Task DemoteOtherAdminsAsync(BlueberryMartApiFactory factory, string keepEmail)
    {
        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var others = await ctx.Users
            .Where(u => u.Role == "admin" && u.Email != keepEmail.ToLower())
            .ToListAsync();
        foreach (var u in others) u.Role = "customer";
        await ctx.SaveChangesAsync();
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
