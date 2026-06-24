using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    // Registers a fresh customer (optionally with a phone), verifies the email, and returns its token.
    private async Task<string> RegisterCustomerAsync(string? phone = null)
    {
        var email = $"link_{Guid.NewGuid():N}@blueberrymart.com";
        return await TestHelpers.RegisterAndVerifyAsync(_factory, _client, email, "secret123", phone);
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

    private Task<HttpResponseMessage> DeleteAccount(string token) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/profile").WithBearer(token));

    [Fact]
    public async Task DeleteAccount_AnonymizesUser_RemovesPersonalData_KeepsOrder()
    {
        // A customer with a phone, an address, and an order.
        var email = $"del_{Guid.NewGuid():N}@blueberrymart.com";
        var token = await TestHelpers.RegisterAndVerifyAsync(_factory, _client, email, "secret123", RandomPhone());
        var userId = await TestHelpers.GetUserIdByEmailAsync(_factory, email);

        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _downtown, $"Del {Guid.NewGuid():N}", stock: 10);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _downtown, itemId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            db.Addresses.Add(new Address
            {
                UserId = userId,
                Label = "Home",
                AddressLine = "123 Test St",
                City = "Kathmandu",
            });
            await db.SaveChangesAsync();
        }

        var resp = await DeleteAccount(token);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.Null(user.Email);            // PII scrubbed
            Assert.Null(user.Phone);
            Assert.Null(user.PasswordHash);
            Assert.NotNull(user.DeletedAt);     // marked deleted
            Assert.Empty(await db.Addresses.Where(a => a.UserId == userId).ToListAsync()); // personal data gone
            Assert.True(await db.Orders.AnyAsync(o => o.Id == orderId && o.UserId == userId)); // order kept, still linked
        }

        // The outstanding token is rejected immediately (no waiting for expiry).
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/profile").WithBearer(token))).StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_FreesEmailForReRegistration()
    {
        var email = $"reuse_{Guid.NewGuid():N}@blueberrymart.com";
        var token = await TestHelpers.RegisterAndVerifyAsync(_factory, _client, email, "secret123");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(token)).StatusCode);

        // Same email can be registered again after deletion (it was nulled, not held).
        var reRegister = await _client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, reRegister.StatusCode);
    }
}
