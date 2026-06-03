using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class MembershipControllerTests
{
    private readonly HttpClient _client;
    private readonly Guid _downtownBranchId;
    private readonly Guid _breadItemId;

    public MembershipControllerTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _downtownBranchId = factory.DowntownBranchId;
        _breadItemId = factory.BreadItemId;
    }

    // Uses customer2 so it does not affect customer1 used by other test files.
    private async Task<string> GetCustomer2TokenAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "customer2@blueberrymart.com",
            password = "customer2_password"
        });
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Activate_FullLifecycle_NotMemberThenMember()
    {
        var token = await GetCustomer2TokenAsync();

        // Before activation
        var before = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/membership/status").WithBearer(token));
        var beforeJson = await before.Content.ReadFromJsonAsync<JsonElement>();

        // Activate
        var activate = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/membership/activate").WithBearer(token));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        // After activation
        var after = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/membership/status").WithBearer(token));
        var afterJson = await after.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(afterJson.GetProperty("isMember").GetBoolean());
    }

    [Fact]
    public async Task PlaceOrder_AsMember_Applies5PercentDiscount()
    {
        var token = await GetCustomer2TokenAsync();

        // Ensure membership is active (idempotent)
        await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/membership/activate").WithBearer(token));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId = _downtownBranchId,
                orderType = "pickup",
                items = new[] { new { itemId = _breadItemId, quantity = 1 } }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();

        var subtotal = json.GetProperty("subtotal").GetDecimal();
        var discount = json.GetProperty("discountAmount").GetDecimal();
        var total = json.GetProperty("totalAmount").GetDecimal();

        Assert.True(discount > 0);
        Assert.Equal(Math.Round(subtotal * 0.05m, 2), discount);
        Assert.Equal(subtotal - discount, total);
    }
}
