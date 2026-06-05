using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Used when Kafka isn't configured (production today, and all tests): events are
/// simply dropped, so nothing depends on a broker being present.
/// </summary>
public sealed class NoOpStockEventProducer : IStockEventProducer
{
    public void Publish(StockChangedEvent evt) { }
}
