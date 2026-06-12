namespace BlueberryMart.Api.Models.Entities;

/// <summary>
/// Transactional outbox row. Domain events (e.g. sales events) are written here in the
/// same DB transaction as the change that produced them, so an event can never be lost or
/// orphaned. A background dispatcher (<c>OutboxDispatcher</c>, worker-only) publishes
/// unpublished rows to Kafka and stamps <see cref="PublishedAt"/>.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Kafka topic to publish to.</summary>
    public string Topic { get; set; } = null!;

    /// <summary>Partition key (e.g. order id) so related events stay ordered.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Serialized event envelope (the Kafka message value).</summary>
    public string Payload { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    /// <summary>Null until the dispatcher has published it to Kafka.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Number of publish attempts (incremented as the dispatcher works through it).</summary>
    public int Attempts { get; set; }
}
