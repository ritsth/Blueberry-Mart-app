using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class OrdersControllerTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _downtownBranchId;
    private readonly Guid _eggsItemId;
    private readonly Guid _milkItemId;

    public OrdersControllerTests(BlueberryMartApiFactory factory)
    {
        _factory          = factory;
        _client           = factory.CreateClient();
        _downtownBranchId = factory.DowntownBranchId;
        _eggsItemId       = factory.EggsItemId;
        _milkItemId       = factory.MilkItemId;
    }

    [Fact]
    public async Task PlaceOrder_ValidRequest_Returns201WithExpectedFields()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var req   = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId  = _downtownBranchId,
                orderType = "pickup",
                items     = new[] { new { itemId = _eggsItemId, quantity = 1 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("totalAmount").GetDecimal() > 0);
        Assert.True(json.GetProperty("loyaltyPointsEarned").GetInt32() > 0);
        // A human-friendly sequential order number is assigned (sequence starts at 1001)
        Assert.True(json.GetProperty("orderNumber").GetInt32() >= 1001);
    }

    [Fact]
    public async Task PlaceOrder_InsufficientStock_ReturnsConflict()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var req   = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId  = _downtownBranchId,
                orderType = "pickup",
                items     = new[] { new { itemId = _eggsItemId, quantity = 999_999 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_InvalidItemId_ReturnsNotFound()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var req   = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId  = _downtownBranchId,
                orderType = "pickup",
                items     = new[] { new { itemId = Guid.NewGuid(), quantity = 1 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_ShareholderToken_ReturnsCreated()
    {
        // Shareholders can also place orders (they can shop too)
        var token = await TestHelpers.GetShareholderTokenAsync(_client);
        var req   = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId  = _downtownBranchId,
                orderType = "pickup",
                items     = new[] { new { itemId = _milkItemId, quantity = 1 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_EmptyItems_ReturnsBadRequest()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var req   = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId  = _downtownBranchId,
                orderType = "pickup",
                items     = Array.Empty<object>()
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_SameIdempotencyKey_ReturnsOriginalOrderAndDeductsStockOnce()
    {
        var token  = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _downtownBranchId, $"Idem {Guid.NewGuid():N}", stock: 5);
        var key    = Guid.NewGuid().ToString();

        HttpRequestMessage Build()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new
                {
                    branchId  = _downtownBranchId,
                    orderType = "pickup",
                    items     = new[] { new { itemId, quantity = 2 } }
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Idempotency-Key", key);
            return req;
        }

        var first  = await _client.SendAsync(Build());
        var second = await _client.SendAsync(Build());   // same key — a double-tap / retry

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);   // replayed, not newly created

        var firstJson  = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstJson.GetProperty("id").GetGuid(), secondJson.GetProperty("id").GetGuid());
        Assert.Equal(firstJson.GetProperty("orderNumber").GetInt32(),
                     secondJson.GetProperty("orderNumber").GetInt32());

        // Stock was deducted exactly once: 5 − 2 = 3 (a duplicate would have dropped it to 1).
        Assert.Equal(3, await TestHelpers.GetStockAsync(_factory, itemId));
    }

    [Fact]
    public async Task PlaceOrder_DifferentIdempotencyKeys_CreateSeparateOrders()
    {
        var token  = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _downtownBranchId, $"Idem {Guid.NewGuid():N}", stock: 5);

        async Task<Guid> PlaceWithKey(string key)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new
                {
                    branchId  = _downtownBranchId,
                    orderType = "pickup",
                    items     = new[] { new { itemId, quantity = 1 } }
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Idempotency-Key", key);
            var resp = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("id").GetGuid();
        }

        var a = await PlaceWithKey(Guid.NewGuid().ToString());
        var b = await PlaceWithKey(Guid.NewGuid().ToString());
        Assert.NotEqual(a, b);   // distinct keys are distinct orders
    }

    [Fact]
    public async Task GetOrder_OwnOrder_ReturnsStatusAndUnpaidPayment()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtownBranchId, _eggsItemId);

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}").WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", json.GetProperty("status").GetString());
        // No payment has been initiated yet, so payment is null.
        Assert.Equal(JsonValueKind.Null, json.GetProperty("payment").ValueKind);
    }

    [Fact]
    public async Task GetOrder_AnotherUsersOrder_ReturnsNotFound()
    {
        // Shareholder places an order; a customer must not be able to read it back.
        var ownerToken = await TestHelpers.GetShareholderTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, ownerToken, _downtownBranchId, _milkItemId);

        var otherToken = await TestHelpers.GetCustomerTokenAsync(_client);
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}").WithBearer(otherToken);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetOrder_WithoutAuth_ReturnsUnauthorized()
    {
        var resp = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task MarkReceived_ReadyOrder_BecomesCompleted()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtownBranchId, _eggsItemId);
        await TestHelpers.SetOrderStatusAsync(_factory, orderId, "ready");

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/receive").WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("completed", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MarkReceived_ConfirmedOrder_ReturnsConflict()
    {
        // Not yet 'ready' (still being prepared) — the customer can't complete it.
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtownBranchId, _eggsItemId);
        await TestHelpers.SetOrderStatusAsync(_factory, orderId, "confirmed");

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/receive").WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task MarkReceived_PendingOrder_ReturnsConflict()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtownBranchId, _eggsItemId);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/receive").WithBearer(token);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_OwnPendingOrder_CancelsAndRestocks()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _downtownBranchId, $"Cancel {Guid.NewGuid():N}", stock: 5);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtownBranchId, itemId, quantity: 2);
        Assert.Equal(3, await TestHelpers.GetStockAsync(_factory, itemId));   // reserved at placement

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cancelled", json.GetProperty("status").GetString());
        Assert.Equal(5, await TestHelpers.GetStockAsync(_factory, itemId));   // restocked
    }

    [Fact]
    public async Task Cancel_PaidConfirmedOrder_ReturnsConflict()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtownBranchId, _eggsItemId);
        await TestHelpers.MarkOrderPaidAsync(_factory, orderId);   // -> confirmed + completed payment

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel").WithBearer(token));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Cancel_AnotherUsersOrder_ReturnsNotFound()
    {
        var owner = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, owner, _downtownBranchId, _eggsItemId);

        var other = await TestHelpers.GetTokenAsync(_client, "customer2@blueberrymart.com", "customer2_password");
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel").WithBearer(other));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
