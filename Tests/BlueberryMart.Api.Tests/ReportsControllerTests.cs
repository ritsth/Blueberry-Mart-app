using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class ReportsControllerTests
{
    private readonly HttpClient _client;
    private readonly BlueberryMartApiFactory _factory;
    private readonly Guid _downtown;

    public ReportsControllerTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
        _downtown = factory.DowntownBranchId;
    }

    private async Task<string> RoleTokenAsync(string role, Guid branchId)
    {
        var email = $"{role}_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, email, "pw", role, branchId);
        return await TestHelpers.GetTokenAsync(_client, email, "pw");
    }

    [Fact]
    public async Task Reports_Staff_Forbidden()
    {
        var staff = await RoleTokenAsync("staff", _downtown);

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/reports/sales").WithBearer(staff));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Reports_Manager_ReflectsPaidOrder()
    {
        // Place an order and mark it paid so it counts as revenue.
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Report {Guid.NewGuid():N}", stock: 50);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, customer, _downtown, itemId, quantity: 2);
        var staff = await RoleTokenAsync("staff", _downtown);
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/orders/manage/{orderId}/record-payment")
        {
            Content = JsonContent.Create(new { method = "cash" }),
        }.WithBearer(staff));

        var manager = await RoleTokenAsync("manager", _downtown);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/reports/sales").WithBearer(manager));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("orderCount").GetInt32() >= 1);
        Assert.True(json.GetProperty("totalRevenue").GetDecimal() > 0);
    }

    [Fact]
    public async Task Reports_RespectsDateRange()
    {
        // Place + pay an order today.
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Range {Guid.NewGuid():N}", stock: 20);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, customer, _downtown, itemId, quantity: 1);
        var staff = await RoleTokenAsync("staff", _downtown);
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/orders/manage/{orderId}/record-payment")
        {
            Content = JsonContent.Create(new { method = "cash" }),
        }.WithBearer(staff));

        var manager = await RoleTokenAsync("manager", _downtown);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // A same-day range (from = to = today) must include today's order — proves the
        // end of the "to" day is inclusive.
        var inRange = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/reports/sales?from={today}&to={today}").WithBearer(manager));
        var inJson = await inRange.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(inJson.GetProperty("orderCount").GetInt32() >= 1);

        // A range entirely in the past must exclude it.
        var outRange = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/reports/sales?from=2000-01-01&to=2000-01-07").WithBearer(manager));
        var outJson = await outRange.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, outJson.GetProperty("orderCount").GetInt32());
    }
}
