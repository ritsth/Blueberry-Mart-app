using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class SalesEventOutboxTests
{
    private readonly BlueberryMartApiFactory _factory;
    private readonly HttpClient _client;

    public SalesEventOutboxTests(BlueberryMartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PlaceOrder_StagesOrderPlacedEventInOutbox()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(
            _client, token, _factory.DowntownBranchId, _factory.EggsItemId, quantity: 2);

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();

        // Exactly one outbox row for this order, on the sales topic, not yet published
        // (the dispatcher only runs on the worker).
        var row = await ctx.OutboxMessages
            .SingleOrDefaultAsync(m => m.Key == orderId.ToString() && m.Topic == "sales.events");
        Assert.NotNull(row);
        Assert.Null(row!.PublishedAt);

        var envelope = JsonSerializer.Deserialize<SalesEventEnvelope>(row.Payload)!;
        Assert.Equal(SalesEventTypes.OrderPlaced, envelope.Type);

        var evt = JsonSerializer.Deserialize<OrderPlacedEvent>(envelope.Data)!;
        Assert.Equal(orderId, evt.OrderId);
        Assert.True(evt.OrderNumber >= 1001);          // DB-generated number was captured
        Assert.Equal(_factory.EggsItemId, Assert.Single(evt.Lines).ItemId);
        Assert.Equal(2, evt.Lines[0].Quantity);
        Assert.Equal(1, evt.Lines[0].Rn);              // sole line is the primary line
    }

    [Fact]
    public async Task SubmitReview_StagesReviewChangedEventInOutbox()
    {
        var token = await TestHelpers.GetCustomerTokenAsync(_client);
        var orderId = await TestHelpers.PlaceOrderAsync(_client, token, _factory.DowntownBranchId, _factory.EggsItemId);
        await TestHelpers.SetOrderStatusAsync(_factory, orderId, "completed");

        var form = new MultipartFormDataContent
        {
            { new StringContent(orderId.ToString()), "orderId" },
            { new StringContent(_factory.EggsItemId.ToString()), "itemId" },
            { new StringContent("5"), "rating" },
            { new StringContent("Great eggs"), "comment" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/reviews") { Content = form }.WithBearer(token);
        var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var row = await ctx.OutboxMessages
            .Where(m => m.Key == orderId.ToString() && m.Topic == "sales.events")
            .ToListAsync();

        var reviewEnvelopes = row
            .Select(m => JsonSerializer.Deserialize<SalesEventEnvelope>(m.Payload)!)
            .Where(e => e.Type == SalesEventTypes.ReviewChanged)
            .ToList();
        var review = Assert.Single(reviewEnvelopes);
        var evt = JsonSerializer.Deserialize<ReviewChangedEvent>(review.Data)!;
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(_factory.EggsItemId, evt.ItemId);
        Assert.Equal(5, evt.Rating);
    }
}
