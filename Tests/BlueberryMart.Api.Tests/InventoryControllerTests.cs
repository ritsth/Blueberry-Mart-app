using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class InventoryControllerTests
{
    private readonly HttpClient _client;
    private readonly Guid _downtownBranchId;

    public InventoryControllerTests(BlueberryMartApiFactory factory)
    {
        _client           = factory.CreateClient();
        _downtownBranchId = factory.DowntownBranchId;
    }

    [Fact]
    public async Task GetCustomerInventory_CustomerToken_ReturnsOnlyInStockNonBulkItems()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp  = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/customer?branchId={_downtownBranchId}")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(items);
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.True(item.GetProperty("stockQuantity").GetInt32() > 0);
            Assert.False(item.GetProperty("isBulkOnly").GetBoolean());
        });
    }

    [Fact]
    public async Task GetCustomerInventory_ShareholderToken_ReturnsForbidden()
    {
        var token = await TestHelpers.GetShareholderTokenAsync(_client);
        var resp  = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/customer?branchId={_downtownBranchId}")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetCustomerInventory_NoToken_ReturnsUnauthorized()
    {
        var resp = await _client.GetAsync($"/api/inventory/customer?branchId={_downtownBranchId}");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetShareholderInventory_ShareholderToken_ReturnsAllItems()
    {
        var token = await TestHelpers.GetShareholderTokenAsync(_client);
        var resp  = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/inventory/shareholder")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(items);
        Assert.True(items.Length >= 13);
    }

    [Fact]
    public async Task GetShareholderInventory_CustomerToken_ReturnsForbidden()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp  = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/inventory/shareholder")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
