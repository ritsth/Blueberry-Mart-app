using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Services;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests;

/// <summary>
/// Covers the backward-compatible pagination caps, the readiness probe, and the outbox retention
/// prune added in the perf/security batch.
/// </summary>
[Collection("Integration")]
public class PerfBatchTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public PerfBatchTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    [Fact]
    public async Task Notifications_RespectLimit_AndUnreadCountsBeyondThePage()
    {
        var email = $"notif_{Guid.NewGuid():N}@example.com";
        var userId = await TestHelpers.CreateUserAsync(_factory, email, "pw123456");
        var token = await TestHelpers.GetTokenAsync(_client, email, "pw123456");

        // Seed 5 notifications for this user: 4 unread, 1 read.
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            for (var i = 0; i < 5; i++)
                ctx.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Message = $"note {i}",
                    IsRead = i == 0,   // one read, four unread
                    CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                });
            await ctx.SaveChangesAsync();
        }

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/notifications?limit=2").WithBearer(token));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // The page is capped at 2…
        Assert.Equal(2, body.GetProperty("notifications").GetArrayLength());
        // …but unread reflects all 4, not just what's on the page.
        Assert.Equal(4, body.GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task Notifications_Offset_SkipsRows()
    {
        var email = $"notifoffset_{Guid.NewGuid():N}@example.com";
        var userId = await TestHelpers.CreateUserAsync(_factory, email, "pw123456");
        var token = await TestHelpers.GetTokenAsync(_client, email, "pw123456");

        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            for (var i = 0; i < 3; i++)
                ctx.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Message = $"m{i}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-i),   // m0 newest
                });
            await ctx.SaveChangesAsync();
        }

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/notifications?limit=1&offset=1").WithBearer(token));
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var page = body.GetProperty("notifications");

        Assert.Equal(1, page.GetArrayLength());
        // Newest-first, offset 1 → the second-newest ("m1").
        Assert.Equal("m1", page[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task ShareholderInventory_CapsResults_ToLimit()
    {
        var token = await TestHelpers.GetShareholderTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/inventory/shareholder?limit=1").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(items);
        Assert.True(items!.Length <= 1);
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WhenDbReachable()
    {
        var resp = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task OutboxPrune_DeletesOldPublished_KeepsRecentAndUnpublished()
    {
        Guid oldPublished, recentPublished, unpublished;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            var now = DateTime.UtcNow;

            var a = new OutboxMessage { Id = Guid.NewGuid(), Topic = "t", Key = "k", Payload = "{}", CreatedAt = now.AddDays(-10), PublishedAt = now.AddDays(-10) };
            var b = new OutboxMessage { Id = Guid.NewGuid(), Topic = "t", Key = "k", Payload = "{}", CreatedAt = now, PublishedAt = now };
            var c = new OutboxMessage { Id = Guid.NewGuid(), Topic = "t", Key = "k", Payload = "{}", CreatedAt = now.AddDays(-10), PublishedAt = null };
            ctx.OutboxMessages.AddRange(a, b, c);
            await ctx.SaveChangesAsync();
            oldPublished = a.Id; recentPublished = b.Id; unpublished = c.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            var deleted = await OutboxDispatcher.PruneOldPublishedAsync(ctx, TimeSpan.FromDays(7));
            Assert.True(deleted >= 1);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            Assert.False(await ctx.OutboxMessages.AnyAsync(m => m.Id == oldPublished), "old published row should be pruned");
            Assert.True(await ctx.OutboxMessages.AnyAsync(m => m.Id == recentPublished), "recent published row should remain");
            Assert.True(await ctx.OutboxMessages.AnyAsync(m => m.Id == unpublished), "unpublished row should always remain");
        }
    }
}
