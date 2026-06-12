using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class ShareholderControllerTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public ShareholderControllerTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAnalytics_ShareholderToken_ReturnsAllMetricFields()
    {
        var token = await TestHelpers.GetShareholderTokenAsync(_client);
        var req   = new HttpRequestMessage(HttpMethod.Get, "/api/shareholders/analytics")
            .WithBearer(token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("totalRevenue",    out _));
        Assert.True(json.TryGetProperty("revenueByBranch", out _));
        Assert.True(json.TryGetProperty("topSellingItems", out _));
        Assert.True(json.TryGetProperty("lowStockAlerts",  out var alerts));

        // Seed data has 4 items with stockQuantity = 0
        Assert.True(alerts.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task GetAnalytics_TotalRevenue_CountsOnlyCollectedOrders()
    {
        var shToken = await TestHelpers.GetShareholderTokenAsync(_client);
        var custToken = await TestHelpers.GetCustomerTokenAsync(_client);

        // A fresh, well-stocked item so we don't exhaust seeded stock.
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _factory.DowntownBranchId, $"RevTest {Guid.NewGuid():N}", stock: 100);

        var before = await TotalRevenueAsync(shToken);

        // Placed but unpaid → must NOT count.
        var orderId = await TestHelpers.PlaceOrderAsync(_client, custToken, _factory.DowntownBranchId, itemId, quantity: 2);
        Assert.Equal(before, await TotalRevenueAsync(shToken));

        // Paid → counts.
        var amount = await TestHelpers.MarkOrderPaidAsync(_factory, orderId);
        Assert.True(amount > 0);
        Assert.Equal(before + amount, await TotalRevenueAsync(shToken));

        // Cancelled (refund) → drops back out.
        await TestHelpers.SetOrderStatusAsync(_factory, orderId, "cancelled");
        Assert.Equal(before, await TotalRevenueAsync(shToken));
    }

    private async Task<decimal> TotalRevenueAsync(string token)
    {
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/shareholders/analytics").WithBearer(token));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("totalRevenue").GetDecimal();
    }

    [Fact]
    public async Task GetAnalytics_CustomerToken_ReturnsForbidden()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp  = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/shareholders/analytics")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetAnalytics_NoToken_ReturnsUnauthorized()
    {
        var resp = await _client.GetAsync("/api/shareholders/analytics");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
