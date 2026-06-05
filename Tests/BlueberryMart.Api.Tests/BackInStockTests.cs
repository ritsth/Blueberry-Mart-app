using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class BackInStockTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public BackInStockTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Restock_AsShareholder_IncreasesStock()
    {
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _factory.SuburbsBranchId, "Restock Widget", stock: 0);
        var token = await TestHelpers.GetShareholderTokenAsync(_client);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/{itemId}/restock")
        { Content = JsonContent.Create(new { quantity = 12 }) }.WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12, json.GetProperty("stockQuantity").GetInt32());
    }

    [Fact]
    public async Task Restock_AsCustomer_ReturnsForbidden()
    {
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _factory.SuburbsBranchId, "Customer Restock Widget");
        var token = await TestHelpers.GetCustomerTokenAsync(_client);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/{itemId}/restock")
        { Content = JsonContent.Create(new { quantity = 5 }) }.WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NotifyMe_OutOfStockItem_Subscribes()
    {
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _factory.SuburbsBranchId, "Sold Out Widget", stock: 0);
        var token = await TestHelpers.GetCustomerTokenAsync(_client);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/{itemId}/notify-me").WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task NotifyMe_InStockItem_ReturnsConflict()
    {
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _factory.SuburbsBranchId, "In Stock Widget", stock: 7);
        var token = await TestHelpers.GetCustomerTokenAsync(_client);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/{itemId}/notify-me").WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Notifications_ReturnsUnreadCountAndList()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/notifications").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("unread").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Array, json.GetProperty("notifications").ValueKind);
    }
}
