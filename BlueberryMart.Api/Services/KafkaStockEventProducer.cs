using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Services.Interfaces;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Publishes stock-change events to Kafka using a single shared producer (the
/// Confluent producer is thread-safe and meant to be reused for the app's lifetime).
/// Keyed by branch+item so all events for the same item at a branch keep their order.
/// </summary>
public sealed class KafkaStockEventProducer : IStockEventProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;
    private readonly ILogger<KafkaStockEventProducer> _logger;

    public KafkaStockEventProducer(IOptions<KafkaOptions> options, ILogger<KafkaStockEventProducer> logger)
    {
        var opts = options.Value;
        _topic = opts.StockChangedTopic;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            AllowAutoCreateTopics = true,   // let the dev broker create the topic on first publish
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public void Publish(StockChangedEvent evt)
    {
        var key = $"{evt.BranchId}:{evt.ItemId}";
        var value = JsonSerializer.Serialize(evt);
        try
        {
            // Produce() is non-blocking: it enqueues and returns immediately; delivery
            // (or failure) is reported asynchronously via the handler below.
            _producer.Produce(_topic, new Message<string, string> { Key = key, Value = value },
                report =>
                {
                    if (report.Error.IsError)
                        _logger.LogWarning("Kafka delivery failed for {Topic}: {Reason}", _topic, report.Error.Reason);
                });
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex, "Could not enqueue stock-changed event for {ItemId}", evt.ItemId);
        }
    }

    public void Dispose()
    {
        // Flush any buffered messages on shutdown so we don't lose in-flight events.
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
