using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services.Interfaces;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests;

/// <summary>
/// Exercises the distributed-cache layer end-to-end (in-memory backend in tests): inventory reads
/// are cache-aside, a write evicts the branch, and the cached auth status never delays a ban.
/// </summary>
[Collection("Integration")]
public class CacheBehaviorTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _branchId;

    public CacheBehaviorTests(BlueberryMartApiFactory factory)
    {
        _factory  = factory;
        _client   = factory.CreateClient();
        _branchId = factory.DowntownBranchId;
    }

    [Fact]
    public async Task CustomerCatalog_IsServedFromCache_UntilBranchIsInvalidated()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _branchId, $"CacheAside {Guid.NewGuid():N}", stock: 50);

        // First read populates the cache.
        Assert.Equal(50, await ReadStockFromCatalogAsync(token, itemId));

        // Change stock with a raw UPDATE — no SaveChanges, so the invalidation interceptor does NOT
        // fire and the cached catalogue stays in force.
        await RawSetStockAsync(itemId, 99);

        // Still 50 → the response came from cache, not a fresh DB query.
        Assert.Equal(50, await ReadStockFromCatalogAsync(token, itemId));

        // Explicitly evict the branch (what any real inventory write triggers) → next read is fresh.
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICacheService>()
                .InvalidateBranchInventoryAsync(_branchId);

        Assert.Equal(99, await ReadStockFromCatalogAsync(token, itemId));
    }

    [Fact]
    public async Task InventoryWrite_EvictsCache_SoNextReadReflectsIt()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _branchId, $"CacheEvict {Guid.NewGuid():N}", stock: 50);

        Assert.Equal(50, await ReadStockFromCatalogAsync(token, itemId));   // caches the catalogue

        // SetStockAsync goes through SaveChangesAsync → the interceptor evicts the branch.
        await TestHelpers.SetStockAsync(_factory, itemId, 77);

        Assert.Equal(77, await ReadStockFromCatalogAsync(token, itemId));   // fresh, not stale
    }

    [Fact]
    public async Task BanningUser_RevokesImmediately_EvenAfterStatusWasCached()
    {
        var email = $"bancache_{Guid.NewGuid():N}@example.com";
        var userId = await TestHelpers.CreateUserAsync(_factory, email, "pw123456");
        var token = await TestHelpers.GetTokenAsync(_client, email, "pw123456");

        // An authed request first, so the "not banned" status is cached.
        var before = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/profile").WithBearer(token));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Ban via the admin endpoint (SaveChangesAsync → interceptor evicts the cached auth status).
        var adminToken = await TestHelpers.GetAdminTokenAsync(_client);
        var ban = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/ban")
            {
                Content = JsonContent.Create(new { reason = "cache revocation test" })
            }.WithBearer(adminToken));
        Assert.Equal(HttpStatusCode.OK, ban.StatusCode);

        // The very next request with the same token is rejected — the cache didn't delay revocation.
        var after = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/profile").WithBearer(token));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private async Task<int> ReadStockFromCatalogAsync(string token, Guid itemId)
    {
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/customer?branchId={_branchId}")
            .WithBearer(token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        var item = items!.Single(i => i.GetProperty("id").GetString() == itemId.ToString());
        return item.GetProperty("stockQuantity").GetInt32();
    }

    private async Task RawSetStockAsync(Guid itemId, int stock)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        // ExecuteUpdate bypasses the ChangeTracker/SaveChanges, so the cache is deliberately NOT
        // invalidated — that's the point of this helper.
        await ctx.Inventory.Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.StockQuantity, stock));
    }
}
