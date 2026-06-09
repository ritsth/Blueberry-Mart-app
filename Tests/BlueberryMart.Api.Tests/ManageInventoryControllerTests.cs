using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class ManageInventoryControllerTests
{
    private readonly HttpClient _client;
    private readonly BlueberryMartApiFactory _factory;
    private readonly Guid _downtown;
    private readonly Guid _suburbs;

    public ManageInventoryControllerTests(BlueberryMartApiFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
        _downtown = factory.DowntownBranchId;
        _suburbs = factory.SuburbsBranchId;
    }

    // A fresh staff account assigned to a branch, returning its login token (carries the branch claim).
    private async Task<string> StaffTokenAsync(Guid branchId)
    {
        var email = $"staff_{Guid.NewGuid():N}@blueberrymart.com";
        await TestHelpers.CreateUserAsync(_factory, email, "staff_pw", "staff", branchId);
        return await TestHelpers.GetTokenAsync(_client, email, "staff_pw");
    }

    [Fact]
    public async Task Staff_CanCreateItem_InOwnBranch()
    {
        var token = await StaffTokenAsync(_downtown);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/manage")
        {
            Content = JsonContent.Create(new
            {
                branchId = _downtown,
                itemName = "Test Mangoes",
                price = 250,
                stockQuantity = 10,
                isBulkOnly = false,
            }),
        }.WithBearer(token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, json.GetProperty("stockQuantity").GetInt32());
        Assert.True(json.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Staff_CannotCreateItem_InAnotherBranch()
    {
        var token = await StaffTokenAsync(_downtown);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/manage")
        {
            Content = JsonContent.Create(new
            {
                branchId = _suburbs,
                itemName = "Cross-branch Item",
                price = 100,
                stockQuantity = 5,
                isBulkOnly = false,
            }),
        }.WithBearer(token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Staff_CanAdjustStock_InOwnBranch()
    {
        var token = await StaffTokenAsync(_downtown);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Adjust {Guid.NewGuid():N}", stock: 2);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/manage/{itemId}/adjust")
        {
            Content = JsonContent.Create(new { delta = 5, reason = "restock" }),
        }.WithBearer(token);

        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(7, json.GetProperty("stockQuantity").GetInt32());
    }

    [Fact]
    public async Task Staff_CannotDeactivate_ManagerOnly()
    {
        var token = await StaffTokenAsync(_downtown);
        var itemId = await TestHelpers.CreateInventoryItemAsync(_factory, _downtown, $"Deact {Guid.NewGuid():N}", stock: 3);

        var resp = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/manage/{itemId}/deactivate").WithBearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
