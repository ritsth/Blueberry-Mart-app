using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class OrdersControllerTests
{
    private readonly HttpClient _client;
    private readonly Guid _downtownBranchId;
    private readonly Guid _eggsItemId;
    private readonly Guid _milkItemId;

    public OrdersControllerTests(BlueberryMartApiFactory factory)
    {
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
}
