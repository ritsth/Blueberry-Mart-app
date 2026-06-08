using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class AdminControllerTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public AdminControllerTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListUsers_CustomerToken_ReturnsForbidden()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ListUsers_NoToken_ReturnsUnauthorized()
    {
        var resp = await _client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListUsers_AdminToken_ReturnsPagedUsers()
    {
        var token = await TestHelpers.GetAdminTokenAsync(_client);
        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?pageSize=5").WithBearer(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("total").GetInt32() >= 1);
        Assert.True(json.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Ban_ThenBannedUserIsRejectedMidSession()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var email = $"ban-target-{Guid.NewGuid():N}@blueberrymart.com";
        var userId = await TestHelpers.CreateUserAsync(_factory, email, "victim_password");

        // The victim logs in and gets a valid token.
        var victimToken = await TestHelpers.GetTokenAsync(_client, email, "victim_password");
        var before = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/addresses").WithBearer(victimToken));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Admin bans them.
        var ban = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/ban")
            { Content = JsonContent.Create(new { reason = "spam" }) }.WithBearer(admin));
        Assert.Equal(HttpStatusCode.OK, ban.StatusCode);

        // The previously-valid token is now rejected on the very next request.
        var after = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/addresses").WithBearer(victimToken));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // Unban restores access.
        var unban = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{userId}/unban").WithBearer(admin));
        Assert.Equal(HttpStatusCode.OK, unban.StatusCode);
        var restoredToken = await TestHelpers.GetTokenAsync(_client, email, "victim_password");
        var restored = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/addresses").WithBearer(restoredToken));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task Ban_OwnAccount_ReturnsBadRequest()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var me = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/admin/users?search=admin@blueberrymart.com")
            .WithBearer(admin));
        var json = await me.Content.ReadFromJsonAsync<JsonElement>();
        var adminId = json.GetProperty("items")[0].GetProperty("id").GetGuid();

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{adminId}/ban")
            { Content = JsonContent.Create(new { reason = "x" }) }.WithBearer(admin));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Ban_AnotherAdmin_ReturnsBadRequest()
    {
        var admin = await TestHelpers.GetAdminTokenAsync(_client);
        var otherAdminId = await TestHelpers.CreateUserAsync(
            _factory, $"admin2-{Guid.NewGuid():N}@blueberrymart.com", "pw", role: "admin");

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{otherAdminId}/ban")
            { Content = JsonContent.Create(new { reason = "x" }) }.WithBearer(admin));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
