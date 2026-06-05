using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Models.Events;
using Confluent.Kafka;
using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// A second, independent consumer of <c>inventory.stock-changed</c> (its own consumer
/// group) that streams every event into a BigQuery table for analytics. Demonstrates
/// Kafka fan-out: this and the back-in-stock consumer read the same stream
/// independently. Only registered when both Kafka and BigQuery are configured.
/// </summary>
public sealed class BigQueryStockSink(
    IOptions<KafkaOptions> kafka,
    IOptions<BigQueryOptions> bigQuery,
    ILogger<BigQueryStockSink> logger) : BackgroundService
{
    private readonly KafkaOptions _kafka = kafka.Value;
    private readonly BigQueryOptions _bq = bigQuery.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken ct)
    {
        var client = BigQueryClient.Create(_bq.ProjectId);
        var table = client.GetTable(_bq.DatasetId, _bq.TableId);

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = _bq.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_kafka.StockChangedTopic);
        logger.LogInformation("BigQueryStockSink streaming {Topic} -> {Dataset}.{Table}",
            _kafka.StockChangedTopic, _bq.DatasetId, _bq.TableId);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try { result = consumer.Consume(ct); }
                catch (ConsumeException ex) { logger.LogWarning("Consume error: {Reason}", ex.Error.Reason); continue; }

                if (result?.Message?.Value is null) continue;

                try
                {
                    var evt = JsonSerializer.Deserialize<StockChangedEvent>(result.Message.Value);
                    if (evt is not null)
                    {
                        table.InsertRow(new BigQueryInsertRow
                        {
                            { "item_id", evt.ItemId.ToString() },
                            { "branch_id", evt.BranchId.ToString() },
                            { "item_name", evt.ItemName },
                            { "old_quantity", evt.OldQuantity },
                            { "new_quantity", evt.NewQuantity },
                            { "reason", evt.Reason },
                            { "occurred_at", evt.OccurredAt },
                            { "ingested_at", DateTime.UtcNow },
                        });
                    }
                    consumer.Commit(result);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to stream event to BigQuery; will retry");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { consumer.Close(); }
    }
}
