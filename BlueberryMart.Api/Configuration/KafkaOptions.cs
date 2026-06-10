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

    /// <summary>SASL username for a managed broker (Confluent Cloud API key). Empty ⇒ local PLAINTEXT.</summary>
    public string? ApiKey { get; set; }

    /// <summary>SASL password for a managed broker (Confluent Cloud API secret).</summary>
    public string? ApiSecret { get; set; }

    /// <summary>
    /// When true, this process runs the consumers (the dedicated Cloud Run worker).
    /// Defaults true locally (no API key) and false on the prod API service, which only
    /// produces — see <c>Program.cs</c>.
    /// </summary>
    public bool RunConsumers { get; set; }

    /// <summary>Topic that stock-change events are published to.</summary>
    public string StockChangedTopic { get; set; } = "inventory.stock-changed";

    /// <summary>Consumer group for the back-in-stock consumer.</summary>
    public string ConsumerGroup { get; set; } = "blueberrymart-backinstock";
}
