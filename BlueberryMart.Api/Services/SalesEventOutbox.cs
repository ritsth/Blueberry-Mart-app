using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Default <see cref="ISalesEventOutbox"/>. Scoped, so it shares the request's
/// <see cref="BlueberryMartDbContext"/> — the outbox row joins the caller's transaction.
/// </summary>
public sealed class SalesEventOutbox(BlueberryMartDbContext context, IOptions<KafkaOptions> kafka) : ISalesEventOutbox
{
    private readonly string _topic = kafka.Value.SalesTopic;

    public void OrderPlaced(OrderPlacedEvent evt) =>
        Enqueue(evt.OrderId, SalesEventTypes.OrderPlaced, evt);

    public void PaymentStatusChanged(PaymentStatusChangedEvent evt) =>
        Enqueue(evt.OrderId, SalesEventTypes.PaymentStatusChanged, evt);

    public void ReviewChanged(ReviewChangedEvent evt) =>
        Enqueue(evt.OrderId, SalesEventTypes.ReviewChanged, evt);

    public void OrderStatusChanged(OrderStatusChangedEvent evt) =>
        Enqueue(evt.OrderId, SalesEventTypes.OrderStatusChanged, evt);

    private void Enqueue(Guid orderId, string type, object payload)
    {
        var envelope = new SalesEventEnvelope(type, JsonSerializer.Serialize(payload));
        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = _topic,
            Key = orderId.ToString(),
            Payload = JsonSerializer.Serialize(envelope),
            CreatedAt = DateTime.UtcNow,
        });
    }
}
