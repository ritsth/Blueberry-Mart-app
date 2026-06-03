using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class DeliveryTests
{
    private readonly HttpClient _client;
    private readonly Guid _downtownBranchId;
    private readonly Guid _eggsItemId;

    public DeliveryTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _downtownBranchId = factory.DowntownBranchId;
        _eggsItemId = factory.EggsItemId;
    }

    private async Task<Guid> CreateAddressAsync(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/addresses")
        {
            Content = JsonContent.Create(new
            {
                label = "Home",
                addressLine = "123 Test Street",
                city = "Kathmandu",
                phone = "9800000000",
                isDefault = true
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(json.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task PlaceDeliveryOrder_WithoutAddress_ReturnsBadRequest()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId = _downtownBranchId,
                orderType = "delivery",
                items = new[] { new { itemId = _eggsItemId, quantity = 1 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PlaceDeliveryOrder_NonMember_ChargesDeliveryFee()
    {
        // customer1 is never made a member in the suite
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var addressId = await CreateAddressAsync(token);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId = _downtownBranchId,
                orderType = "delivery",
                addressId,
                items = new[] { new { itemId = _eggsItemId, quantity = 1 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("delivery", json.GetProperty("orderType").GetString());
        Assert.Equal(100m, json.GetProperty("deliveryFee").GetDecimal());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("deliveryAddress").GetString()));
    }

    [Fact]
    public async Task GetAddresses_ReturnsCreatedAddress()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        await CreateAddressAsync(token);

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/addresses").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetArrayLength() >= 1);
    }
}
