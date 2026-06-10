using BlueberryMart.Api.Data;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class DataSeederTests
{
    private const string SeedDomain = "@seed.blueberrymart.com";
    private readonly BlueberryMartApiFactory _factory;

    public DataSeederTests(BlueberryMartApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Seeder_CreatesThenClearsData()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();

        await DataSeeder.RunAsync(ctx, ["seed", "--customers", "4", "--orders", "15", "--days", "30"]);

        var seedUserIds = await ctx.Users.Where(u => u.Email.EndsWith(SeedDomain)).Select(u => u.Id).ToListAsync();
        Assert.True(seedUserIds.Count >= 4);
        Assert.True(await ctx.Orders.CountAsync(o => seedUserIds.Contains(o.UserId)) >= 15);
        // Paid orders produced a payment row.
        Assert.True(await ctx.Payments.CountAsync(p => ctx.Orders
            .Where(o => seedUserIds.Contains(o.UserId)).Select(o => o.Id).Contains(p.OrderId)) >= 1);

        await DataSeeder.RunAsync(ctx, ["seed", "clear"]);

        Assert.Equal(0, await ctx.Users.CountAsync(u => u.Email.EndsWith(SeedDomain)));
    }
}
