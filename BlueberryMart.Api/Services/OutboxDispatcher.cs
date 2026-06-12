using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Data;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Publishes transactional-outbox rows to Kafka and stamps them published. Runs only on the
/// always-on, single-instance worker (gated by <c>Kafka:RunConsumers</c> in <c>Program.cs</c>),
/// so a simple "scan unpublished, publish, mark" loop is safe — no two dispatchers race.
/// (A multi-instance deployment would need <c>SELECT … FOR UPDATE SKIP LOCKED</c> instead.)
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<KafkaOptions> kafka,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = kafka.Value;
        var config = new ProducerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            AllowAutoCreateTopics = true,   // dev broker creates the topic on first publish
        }.WithSecurity(opts);
        using var producer = new ProducerBuilder<string, string>(config).Build();
        logger.LogInformation("OutboxDispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishBatchAsync(producer, stoppingToken);
                // Nothing pending → back off; otherwise keep draining promptly.
                if (published == 0)
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch loop error; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        producer.Flush(TimeSpan.FromSeconds(5));
    }

    private async Task<int> PublishBatchAsync(IProducer<string, string> producer, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();

        var batch = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (batch.Count == 0) return 0;

        foreach (var m in batch)
        {
            // ProduceAsync awaits broker acknowledgement, so we only mark a row published
            // once it's durably on the topic.
            await producer.ProduceAsync(m.Topic,
                new Message<string, string> { Key = m.Key, Value = m.Payload }, ct);
            m.PublishedAt = DateTime.UtcNow;
            m.Attempts += 1;
        }

        await db.SaveChangesAsync(ct);
        return batch.Count;
    }
}
