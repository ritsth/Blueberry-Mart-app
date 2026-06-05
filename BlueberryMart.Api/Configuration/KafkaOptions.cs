namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Settings for the Kafka inventory event stream, bound from the <c>"Kafka"</c>
/// section. When <see cref="BootstrapServers"/> is empty the app wires up a no-op
/// producer, so Kafka is entirely opt-in (production and tests run without it).
/// </summary>
public class KafkaOptions
{
    /// <summary>e.g. <c>localhost:19092</c> locally. Empty ⇒ Kafka disabled.</summary>
    public string? BootstrapServers { get; set; }

    /// <summary>Topic that stock-change events are published to.</summary>
    public string StockChangedTopic { get; set; } = "inventory.stock-changed";

    /// <summary>Consumer group for the back-in-stock consumer.</summary>
    public string ConsumerGroup { get; set; } = "blueberrymart-backinstock";
}
