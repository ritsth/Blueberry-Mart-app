using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Background consumer of the <c>inventory.stock-changed</c> topic. When an item goes
/// from out-of-stock to in-stock, it creates a back-in-stock notification for every
/// customer who subscribed. Only registered when Kafka is configured.
/// </summary>
public sealed class StockEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<StockEventConsumer> logger) : BackgroundService
{
    private readonly KafkaOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Offload the blocking consume loop so it doesn't hold up app startup.
        await Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,   // read from the start on a fresh group
            EnableAutoCommit = false,                     // we commit manually after processing
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.StockChangedTopic);
        logger.LogInformation("StockEventConsumer subscribed to {Topic} as group {Group}",
            _options.StockChangedTopic, _options.ConsumerGroup);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning("Consume error: {Reason}", ex.Error.Reason);
                    continue;
                }

                if (result?.Message?.Value is null)
                    continue;

                try
                {
                    HandleAsync(result.Message.Value, ct).GetAwaiter().GetResult();
                    // Commit only after successful processing → at-least-once delivery.
                    consumer.Commit(result);
                }
                catch (Exception ex)
                {
                    // Leave the offset uncommitted so the message is retried.
                    logger.LogError(ex, "Failed to process stock event; will retry");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleAsync(string json, CancellationToken ct)
    {
        var evt = JsonSerializer.Deserialize<StockChangedEvent>(json);
        if (evt is null)
            return;

        // Only act on the out-of-stock -> in-stock transition.
        if (!(evt.OldQuantity <= 0 && evt.NewQuantity > 0))
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();

        var subs = await db.StockSubscriptions
            .Where(s => s.InventoryId == evt.ItemId && s.NotifiedAt == null)
            .ToListAsync(ct);
        if (subs.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var sub in subs)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = sub.UserId,
                Message = $"{evt.ItemName} is back in stock!",
                InventoryId = evt.ItemId,
                IsRead = false,
                CreatedAt = now
            });
            sub.NotifiedAt = now;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Back-in-stock: notified {Count} subscriber(s) for {Item}",
            subs.Count, evt.ItemName);
    }
}
