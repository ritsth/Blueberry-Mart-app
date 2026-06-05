using BlueberryMart.Api.Models.Events;

namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>Publishes inventory stock-change events to the event stream (Kafka).</summary>
public interface IStockEventProducer
{
    /// <summary>
    /// Fire-and-forget publish. Never throws into the caller — producing an event
    /// must not break order placement.
    /// </summary>
    void Publish(StockChangedEvent evt);
}
