using System.Net;
using System.Net.Http.Json;
using BlueberryMart.Api.Tests.Infrastructure;

namespace BlueberryMart.Api.Tests;

/// <summary>
/// Guards the pessimistic row-locking (SELECT … FOR UPDATE) on stock deduction: two orders racing
/// for the last unit must not both succeed. Runs against real Postgres, so the lock behaves for real.
/// </summary>
[Collection("Integration")]
public class StockConcurrencyTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _branchId;

    public StockConcurrencyTests(BlueberryMartApiFactory factory)
    {
        _factory  = factory;
        _client   = factory.CreateClient();
        _branchId = factory.DowntownBranchId;
    }

    [Fact]
    public async Task PlaceOrder_TwoConcurrentOrdersForLastUnit_OneSucceedsOneConflicts()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _branchId, $"Race {Guid.NewGuid():N}", stock: 1);

        Task<HttpResponseMessage> PlaceOne() =>
            _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(new
                {
                    branchId  = _branchId,
                    orderType = "pickup",
                    items     = new[] { new { itemId, quantity = 1 } }
                })
            }.WithBearer(token));

        // Fire both at once — each gets its own DbContext/transaction.
        var results = await Task.WhenAll(PlaceOne(), PlaceOne());

        // Exactly one Created and one Conflict. Two successes would mean the last unit was oversold.
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // Stock lands at 0, never negative.
        Assert.Equal(0, await TestHelpers.GetStockAsync(_factory, itemId));
    }

    [Fact]
    public async Task PlaceOrder_ConcurrentWithRestock_NeitherWriteIsLost()
    {
        // An order deducts 3 while a restock adds 10 to the same item, concurrently. With the row
        // lock the two serialise, so the result is exactly start - 3 + 10 (no lost update).
        var customerToken = await TestHelpers.GetCustomerTokenAsync(_client);
        var shareholderToken = await TestHelpers.GetShareholderTokenAsync(_client);
        var itemId = await TestHelpers.CreateInventoryItemAsync(
            _factory, _branchId, $"RaceRestock {Guid.NewGuid():N}", stock: 5);

        var order = _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                branchId  = _branchId,
                orderType = "pickup",
                items     = new[] { new { itemId, quantity = 3 } }
            })
        }.WithBearer(customerToken));

        var restock = _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/{itemId}/restock")
        {
            Content = JsonContent.Create(new { quantity = 10 })
        }.WithBearer(shareholderToken));

        var (orderResp, restockResp) = (await order, await restock);
        Assert.Equal(HttpStatusCode.Created, orderResp.StatusCode);   // new order
        Assert.Equal(HttpStatusCode.OK, restockResp.StatusCode);      // restock

        // 5 - 3 + 10 = 12. A lost update would leave 2 (restock lost) or 15 (order lost).
        Assert.Equal(12, await TestHelpers.GetStockAsync(_factory, itemId));
    }
}
