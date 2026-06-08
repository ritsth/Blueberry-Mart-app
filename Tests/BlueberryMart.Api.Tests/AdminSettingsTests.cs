using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class AdminSettingsTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public AdminSettingsTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task ResetSettingsAsync(string adminToken)
    {
        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new
            {
                deliveryFee = 100m,
                membershipMonthlyFee = 199m,
                memberDiscountRate = 0.05m,
                maintenanceMode = false,
                maintenanceMessage = "",
            })
        }.WithBearer(adminToken));
    }

    [Fact]
    public async Task GetSettings_NonAdmin_Forbidden()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings").WithBearer(token));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_RoundTripsAndShowsInPublicStatus()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        try
        {
            var put = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
            {
                Content = JsonContent.Create(new { deliveryFee = 250m, membershipMonthlyFee = 299m })
            }.WithBearer(admin));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            // Public, unauthenticated status reflects the new values.
            var status = await _client.GetFromJsonAsync<JsonElement>("/api/system/status");
            Assert.Equal(250m, status.GetProperty("deliveryFee").GetDecimal());
            Assert.Equal(299m, status.GetProperty("membershipMonthlyFee").GetDecimal());
        }
        finally { await ResetSettingsAsync(admin); }
    }

    [Fact]
    public async Task UpdateSettings_RejectsOutOfRangeDiscount()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
        {
            Content = JsonContent.Create(new { memberDiscountRate = 1.5m })
        }.WithBearer(admin));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task MaintenanceMode_BlocksOrderingThenRestores()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        try
        {
            // Turn maintenance on.
            await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
            {
                Content = JsonContent.Create(new { maintenanceMode = true, maintenanceMessage = "Back at 5pm" })
            }.WithBearer(admin));

            var blocked = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new
                {
                    branchId = _factory.DowntownBranchId,
                    orderType = "pickup",
                    items = new[] { new { itemId = _factory.MilkItemId, quantity = 1 } }
                })
            }.WithBearer(customer));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        }
        finally { await ResetSettingsAsync(admin); }

        // After reset, ordering works again.
        var ok = await TestHelpers.PlaceOrderAsync(
            _client, customer, _factory.DowntownBranchId, _factory.MilkItemId);
        Assert.NotEqual(Guid.Empty, ok);
    }

    [Fact]
    public async Task MembershipStatus_ReflectsEditedFee()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var customer = await TestHelpers.GetCustomerTokenAsync(_client);
        try
        {
            await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/admin/settings")
            {
                Content = JsonContent.Create(new { membershipMonthlyFee = 149m, memberDiscountRate = 0.10m })
            }.WithBearer(admin));

            var status = await _client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/api/membership/status").WithBearer(customer));
            var json = await status.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(149m, json.GetProperty("monthlyFee").GetDecimal());
            Assert.Equal(0.10m, json.GetProperty("discountRate").GetDecimal());
        }
        finally { await ResetSettingsAsync(admin); }
    }

    [Fact]
    public async Task AssignRole_PromotesCustomerToShareholder()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var id = await TestHelpers.CreateUserAsync(
            _factory, $"promote-{Guid.NewGuid():N}@blueberrymart.com", "pw");

        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{id}/role")
        {
            Content = JsonContent.Create(new { role = "shareholder" })
        }.WithBearer(admin));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("shareholder", json.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AssignRole_DemotingLastAdmin_Blocked()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        // Ensure the seeded admin is the only one, so the last-admin guard applies.
        await TestHelpers.DemoteOtherAdminsAsync(_factory, BlueberryMartApiFactory.AdminEmail);
        var me = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin@blueberrymart.com")
            .WithBearer(admin));
        var json = await me.Content.ReadFromJsonAsync<JsonElement>();
        var adminId = json.GetProperty("items")[0].GetProperty("id").GetGuid();

        var resp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{adminId}/role")
        {
            Content = JsonContent.Create(new { role = "customer" })
        }.WithBearer(admin));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
