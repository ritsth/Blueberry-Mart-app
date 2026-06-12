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
    public async Task GetCustomerInventory_ShareholderToken_ReturnsOk()
    {
        // Shareholders can browse customer inventory (they can also shop)
        var token = await TestHelpers.GetShareholderTokenAsync(_client);
        var resp  = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/customer?branchId={_downtownBranchId}")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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

    [Fact]
    public async Task GetBulk_NonMember_ReturnsForbidden()
    {
        // customer1 is never made a member in the suite
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/bulk?branchId={_downtownBranchId}")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetBulk_Member_ReturnsOnlyInStockBulkItems()
    {
        // Use customer2 and activate membership first so they can access bulk
        var login = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "customer2@blueberrymart.com",
            password = "customer2_password"
        });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/membership/activate").WithBearer(token));

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/bulk?branchId={_downtownBranchId}")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(items);
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.True(item.GetProperty("stockQuantity").GetInt32() > 0);
            Assert.True(item.GetProperty("isBulkOnly").GetBoolean());
        });
    }

    [Fact]
    public async Task Top_CustomerToken_ReturnsInStockItems()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/top?branchId={_downtownBranchId}")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(items);
        // Only in-stock items are surfaced as best sellers.
        Assert.All(items!, item => Assert.True(item.GetProperty("stockQuantity").GetInt32() > 0));
    }

    [Fact]
    public async Task Top_Bulk_NonMember_ReturnsForbidden()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/inventory/top?branchId={_downtownBranchId}&bulk=true")
            .WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
