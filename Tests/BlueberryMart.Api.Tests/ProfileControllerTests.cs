using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class ProfileControllerTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _downtown;

    public ProfileControllerTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _downtown = factory.DowntownBranchId;
    }

    private static string RandomPhone() => "98" + Random.Shared.Next(10_000_000, 99_999_999);

    // Registers a fresh customer (optionally with a phone) and returns its bearer token.
    private async Task<string> RegisterCustomerAsync(string? phone = null)
    {
        var email = $"link_{Guid.NewGuid():N}@blueberrymart.com";
        var resp = await _client.PostAsJsonAsync("/api/auth/register", new { email, password = "secret123", phone });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    private Task<HttpResponseMessage> LinkPhone(string token, string phone) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/profile/link-phone")
        { Content = JsonContent.Create(new { phone }) }.WithBearer(token));

    private async Task<JsonElement> ProfileAsync(string token)
    {
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/profile").WithBearer(token));
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task LinkPhone_NoGuest_SetsPhone()
    {
        var token = await RegisterCustomerAsync();
        var phone = RandomPhone();

        var resp = await LinkPhone(token, phone);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(phone, (await ProfileAsync(token)).GetProperty("phone").GetString());
    }

    [Fact]
    public async Task LinkPhone_MergesGuest_TransfersLoyaltyAndOrders()
    {
        // A guest with an in-store purchase (earns loyalty + owns an order).
        var phone = RandomPhone();
        var guestId = await TestHelpers.CreateGuestUserAsync(_factory, phone);
        var staffEmail = $"till_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, staffEmail, "pw", "staff", _downtown);
        var staff = await TestHelpers.GetTokenAsync(_client, staffEmail, "pw");
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Link {Guid.NewGuid():N}", stock: 10);
        var sale = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/orders/manage/in-store-sale")
        {
            Content = JsonContent.Create(new { items = new[] { new { itemId, quantity = 1 } }, paymentMethod = "cash", customerId = guestId })
        }.WithBearer(staff));
        Assert.Equal(HttpStatusCode.OK, sale.StatusCode);
        var guestPts = await TestHelpers.GetLoyaltyPointsAsync(_factory, guestId);
        Assert.True(guestPts > 0);

        // A customer with no phone links that number → claims the guest.
        var token = await RegisterCustomerAsync();
        var resp = await LinkPhone(token, phone);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var profile = await ProfileAsync(token);
        Assert.Equal(phone, profile.GetProperty("phone").GetString());
        Assert.Equal(guestPts, profile.GetProperty("loyaltyPoints").GetInt32());   // loyalty merged in
        Assert.True(profile.GetProperty("totalOrders").GetInt32() >= 1);           // the in-store order moved over
        Assert.False(await TestHelpers.UserExistsAsync(_factory, guestId));        // guest row removed
    }

    [Fact]
    public async Task LinkPhone_PhoneOnFullAccount_ReturnsConflict()
    {
        var phone = RandomPhone();
        await RegisterCustomerAsync(phone);            // a full account now owns this phone
        var other = await RegisterCustomerAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await LinkPhone(other, phone)).StatusCode);
    }

    [Fact]
    public async Task LinkPhone_CallerAlreadyHasPhone_ReturnsConflict()
    {
        var token = await RegisterCustomerAsync(RandomPhone());   // already has a phone
        Assert.Equal(HttpStatusCode.Conflict, (await LinkPhone(token, RandomPhone())).StatusCode);
    }

    [Fact]
    public async Task LinkPhone_TooLong_ReturnsBadRequest()
    {
        var token = await RegisterCustomerAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await LinkPhone(token, "123456789012")).StatusCode);
    }
}
