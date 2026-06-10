using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class DashboardControllerTests
{
    private readonly HttpClient _client;
    private readonly BlueberryMartApiFactory _factory;

    public DashboardControllerTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
    }

    [Fact]
    public async Task Summary_Staff_ReturnsCounts()
    {
        var email = $"staff_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, email, "pw", "staff", _factory.DowntownBranchId);
        var token = await TestHelpers.GetTokenAsync(_client, email, "pw");

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("lowStockItems").GetInt32() >= 0);
        Assert.True(json.GetProperty("pendingOrders").GetInt32() >= 0);
        Assert.True(json.GetProperty("activeOrders").GetInt32() >= 0);
    }

    [Fact]
    public async Task Summary_Customer_Forbidden()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
