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

    // ---- In-store sales ----

    private HttpRequestMessage InStoreSale(object body) =>
        new HttpRequestMessage(HttpMethod.Post, "/api/orders/manage/in-store-sale")
        { Content = JsonContent.Create(body) };

    [Fact]
    public async Task InStoreSale_WalkIn_CreatesCompletedPaidSale_AndDeductsStock()
    {
        var staff = await RoleTokenAsync("staff", _downtown);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Till {Guid.NewGuid():N}", stock: 5);

        var resp = await _client.SendAsync(InStoreSale(new
        {
            items = new[] { new { itemId, quantity = 2 } },
            paymentMethod = "cash"
        }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = Guid.Parse(json.GetProperty("id").GetString()!);
        Assert.Equal("completed", json.GetProperty("status").GetString());
        Assert.Equal("in_store", json.GetProperty("channel").GetString());

        var (status, channel, userId) = await TestHelpers.GetOrderInfoAsync(_factory, orderId);
        Assert.Equal("completed", status);
        Assert.Equal("in_store", channel);
        Assert.Null(userId);                                                  // anonymous walk-in — no customer
        Assert.True(await TestHelpers.OrderHasCompletedPaymentAsync(_factory, orderId));
        Assert.Equal(3, await TestHelpers.GetStockAsync(_factory, itemId));   // 5 - 2
    }

    [Fact]
    public async Task InStoreSale_AttachedCustomer_CreditsLoyalty()
    {
        var staff = await RoleTokenAsync("staff", _downtown);
        var customerId = await TestHelpers.CreateUserAsync(
            _factory, $"instore_{Guid.NewGuid():N}@blueberrymart.com", "pw");
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Till {Guid.NewGuid():N}", stock: 10);
        var before = await TestHelpers.GetLoyaltyPointsAsync(_factory, customerId);

        var resp = await _client.SendAsync(InStoreSale(new
        {
            items = new[] { new { itemId, quantity = 1 } },
            paymentMethod = "card",
            customerId
        }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = Guid.Parse(json.GetProperty("id").GetString()!);
        var (_, _, userId) = await TestHelpers.GetOrderInfoAsync(_factory, orderId);
        Assert.Equal(customerId, userId);                                     // attributed to the customer
        Assert.True(await TestHelpers.GetLoyaltyPointsAsync(_factory, customerId) > before);
    }

    [Fact]
    public async Task InStoreSale_ItemFromAnotherBranch_NotFound()
    {
        // Staff sell only at their own branch; an item from another branch isn't in scope.
        var staff = await RoleTokenAsync("staff", _downtown);
        var suburbsItem = await TestHelpers.CreateInventoryItemAsync(_factory, _suburbs, $"Far {Guid.NewGuid():N}", stock: 5);

        var resp = await _client.SendAsync(InStoreSale(new
        {
            items = new[] { new { itemId = suburbsItem, quantity = 1 } },
            paymentMethod = "cash"
        }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task InStoreSale_InsufficientStock_Conflict()
    {
        var staff = await RoleTokenAsync("staff", _downtown);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Till {Guid.NewGuid():N}", stock: 1);

        var resp = await _client.SendAsync(InStoreSale(new
        {
            items = new[] { new { itemId, quantity = 5 } },
            paymentMethod = "cash"
        }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task InStoreSale_BulkItem_BadRequest()
    {
        // Bulk = members-only wholesale; not sold at the walk-in till.
        var staff = await RoleTokenAsync("staff", _downtown);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _downtown, $"Bulk {Guid.NewGuid():N}", stock: 20, bulk: true);

        var resp = await _client.SendAsync(InStoreSale(new
        {
            items = new[] { new { itemId, quantity = 1 } },
            paymentMethod = "cash"
        }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SearchCustomers_FindsShopperByEmail_ExcludesStaff()
    {
        var staff = await RoleTokenAsync("staff", _downtown);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var customerEmail = $"shopper_{tag}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, customerEmail, "pw");                 // a real shopper
        await TestHelpers.CreateUserAsync(_factory, $"worker_{tag}@blueberrymart.com", "pw", "staff", _downtown);

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/orders/manage/customers?q={tag}").WithBearer(staff));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(rows);
        Assert.Contains(rows!, r => r.GetProperty("email").GetString() == customerEmail);
        Assert.DoesNotContain(rows!, r => r.GetProperty("email").GetString()!.StartsWith("worker_"));
    }

    [Fact]
    public async Task InStoreSale_UnassignedStaff_BadRequest()
    {
        // A staff account with no branch can't ring up a sale (nowhere to sell from).
        var email = $"nobranch_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, email, "pw", "staff", branchId: null);
        var staff = await TestHelpers.GetTokenAsync(_client, email, "pw");
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Till {Guid.NewGuid():N}", stock: 5);

        var resp = await _client.SendAsync(InStoreSale(new
        {
            items = new[] { new { itemId, quantity = 1 } },
            paymentMethod = "cash"
        }).WithBearer(staff));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
