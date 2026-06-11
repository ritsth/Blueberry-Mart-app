using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services.Interfaces;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class OrderExpiryTests
{
    private readonly HttpClient _client;
    private readonly BlueberryMartApiFactory _factory;
    private readonly Guid _downtown;

    public OrderExpiryTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
        _downtown = factory.DowntownBranchId;
    }

    private async Task BackdateOrderAsync(Guid orderId, int minutes)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var order = await ctx.Orders.FirstAsync(o => o.Id == orderId);
        order.CreatedAt = DateTime.UtcNow.AddMinutes(-minutes);
        await ctx.SaveChangesAsync();
    }

    private async Task<int> SweepAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IOrderExpiryService>();
        return await svc.SweepExpiredAsync(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Sweep_CancelsAndRestocks_ExpiredUnpaidOrder()
    {
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Expiry {Guid.NewGuid():N}", stock: 10);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, customer, _downtown, itemId, quantity: 3); // 10 -> 7

        await BackdateOrderAsync(orderId, minutes: 31);
        var expired = await SweepAsync();
        Assert.True(expired >= 1);

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        Assert.Equal("cancelled", (await ctx.Orders.FirstAsync(o => o.Id == orderId)).Status);
        Assert.Equal(10, (await ctx.Inventory.FirstAsync(i => i.Id == itemId)).StockQuantity); // stock returned
    }

    [Fact]
    public async Task Sweep_LeavesRecentPendingOrderAlone()
    {
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Recent {Guid.NewGuid():N}", stock: 10);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, customer, _downtown, itemId, quantity: 2); // created just now

        await SweepAsync(); // within the 30-min hold → must be untouched

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        Assert.Equal("pending", (await ctx.Orders.FirstAsync(o => o.Id == orderId)).Status);
        Assert.Equal(8, (await ctx.Inventory.FirstAsync(i => i.Id == itemId)).StockQuantity); // still reserved
    }
}
