using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class ManageOrdersControllerTests
{
    private readonly HttpClient _client;
    private readonly BlueberryMartApiFactory _factory;
    private readonly Guid _downtown;
    private readonly Guid _suburbs;

    public ManageOrdersControllerTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
        _downtown = factory.DowntownBranchId;
        _suburbs = factory.SuburbsBranchId;
    }

    private async Task<string> RoleTokenAsync(string role, Guid branchId)
    {
        var email = $"{role}_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, email, "pw", role, branchId);
        return await TestHelpers.GetTokenAsync(_client, email, "pw");
    }

    // A fresh pending order in the given branch (own item with stock, placed by a customer).
    private async Task<Guid> PlacePendingOrderAsync(Guid branchId)
    {
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, branchId, $"OrderItem {Guid.NewGuid():N}", stock: 50);
        return await TestHelpers.PlaceOrderAsync(_client, customer, branchId, itemId, quantity: 2);
    }

    private HttpRequestMessage Post(Guid orderId, string action, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/manage/{orderId}/{action}");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task Staff_RecordPayment_ConfirmsOrder()
    {
        var orderId = await PlacePendingOrderAsync(_downtown);
        var staff = await RoleTokenAsync("staff", _downtown);

        var resp = await _client.SendAsync(Post(orderId, "record-payment", new { method = "cash" }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("confirmed", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Staff_AdvanceStatus_ConfirmedToProcessing()
    {
        var orderId = await PlacePendingOrderAsync(_downtown);
        var staff = await RoleTokenAsync("staff", _downtown);
        await _client.SendAsync(Post(orderId, "record-payment", new { method = "cash" }).WithBearer(staff));

        var resp = await _client.SendAsync(Post(orderId, "status", new { status = "processing" }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("processing", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AdvanceStatus_RejectsNonLinearJump()
    {
        var orderId = await PlacePendingOrderAsync(_downtown);
        var staff = await RoleTokenAsync("staff", _downtown);
        await _client.SendAsync(Post(orderId, "record-payment", new { method = "cash" }).WithBearer(staff));

        // confirmed can only go to processing, not straight to completed.
        var resp = await _client.SendAsync(Post(orderId, "status", new { status = "completed" }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Staff_CannotManage_AnotherBranchOrder()
    {
        var orderId = await PlacePendingOrderAsync(_downtown);
        var staff = await RoleTokenAsync("staff", _suburbs);

        var resp = await _client.SendAsync(Post(orderId, "record-payment", new { method = "cash" }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Staff_CannotCancel_ManagerOnly()
    {
        var orderId = await PlacePendingOrderAsync(_downtown);
        var staff = await RoleTokenAsync("staff", _downtown);

        var resp = await _client.SendAsync(Post(orderId, "cancel").WithBearer(staff));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Manager_CanCancel_PendingOrder()
    {
        var orderId = await PlacePendingOrderAsync(_downtown);
        var manager = await RoleTokenAsync("manager", _downtown);

        var resp = await _client.SendAsync(Post(orderId, "cancel").WithBearer(manager));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cancelled", json.GetProperty("status").GetString());
    }
}
