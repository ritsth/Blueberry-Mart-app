using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Models.Events;
using Confluent.Kafka;
using Google.Cloud.BigQuery.V2;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Consumes the <c>sales.events</c> stream and appends each event to the matching raw BigQuery
/// table (<c>sales_order_lines</c> / <c>sales_payment_status</c> / <c>sales_reviews</c>). These
/// are append-only — the <c>sales_fact</c> VIEW reconstitutes current state from them at query
/// time, so we never UPDATE/DELETE (which BigQuery blocks on freshly-streamed rows). Runs only on
/// the worker, when both Kafka and BigQuery are configured.
/// </summary>
public sealed class BigQuerySalesSink(
    IOptions<KafkaOptions> kafka,
    IOptions<BigQueryOptions> bigQuery,
    ILogger<BigQuerySalesSink> logger) : BackgroundService
{
    private readonly KafkaOptions _kafka = kafka.Value;
    private readonly BigQueryOptions _bq = bigQuery.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry the whole loop rather than letting it fault the host — notably, the raw tables
        // may not exist yet right after a deploy (they're created at cutover). GetTable would
        // throw 404 until then; we log and retry instead of taking the worker down.
        await Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { ConsumeLoop(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "BigQuerySalesSink loop error (raw tables not ready yet?); retrying in 30s");
                    try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }, stoppingToken);
    }

    private void ConsumeLoop(CancellationToken ct)
    {
        var client = BigQueryClient.Create(_bq.ProjectId);
        var orderLines = client.GetTable(_bq.DatasetId, _bq.OrderLinesTableId);
        var payments = client.GetTable(_bq.DatasetId, _bq.PaymentStatusTableId);
        var reviews = client.GetTable(_bq.DatasetId, _bq.ReviewsTableId);
        var orderStatus = client.GetTable(_bq.DatasetId, _bq.OrderStatusTableId);

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = _kafka.SalesConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }.WithSecurity(_kafka);   // SASL_SSL for a managed broker; no-op locally

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_kafka.SalesTopic);
        logger.LogInformation("BigQuerySalesSink streaming {Topic} -> {Dataset}.[{Lines},{Pay},{Rev}]",
            _kafka.SalesTopic, _bq.DatasetId, _bq.OrderLinesTableId, _bq.PaymentStatusTableId, _bq.ReviewsTableId);

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
                    Handle(result.Message.Value, orderLines, payments, reviews, orderStatus);
                    // Commit only after a successful insert, so a failed write is retried.
                    consumer.Commit(result);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to stream sales event to BigQuery; will retry");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { consumer.Close(); }
    }

    private static void Handle(string value, BigQueryTable orderLines, BigQueryTable payments, BigQueryTable reviews, BigQueryTable orderStatus)
    {
        var envelope = JsonSerializer.Deserialize<SalesEventEnvelope>(value);
        if (envelope is null) return;

        switch (envelope.Type)
        {
            case SalesEventTypes.OrderPlaced:
                {
                    var e = JsonSerializer.Deserialize<OrderPlacedEvent>(envelope.Data)!;
                    var rows = e.Lines.Select(l => new BigQueryInsertRow
                {
                    { "order_id", e.OrderId.ToString() },
                    { "order_line_id", l.OrderLineId.ToString() },
                    { "order_number", e.OrderNumber },
                    { "occurred_at", e.OccurredAt },
                    { "branch_name", e.BranchName },
                    { "item_id", l.ItemId.ToString() },
                    { "item_name", l.ItemName },
                    { "is_bulk", l.IsBulk },
                    { "order_type", e.OrderType },
                    { "channel", e.Channel },
                    { "is_member", e.IsMember },
                    { "customer_id", e.CustomerId.ToString() },
                    { "quantity", l.Quantity },
                    { "unit_price", l.UnitPrice },
                    { "order_discount", e.OrderDiscount },
                    { "order_delivery_fee", e.OrderDeliveryFee },
                    { "rn", l.Rn },
                });
                    orderLines.InsertRows(rows);
                    break;
                }
            case SalesEventTypes.PaymentStatusChanged:
                {
                    var e = JsonSerializer.Deserialize<PaymentStatusChangedEvent>(envelope.Data)!;
                    payments.InsertRow(new BigQueryInsertRow
                {
                    { "order_id", e.OrderId.ToString() },
                    { "payment_status", e.Status },
                    { "occurred_at", e.OccurredAt },
                });
                    break;
                }
            case SalesEventTypes.ReviewChanged:
                {
                    var e = JsonSerializer.Deserialize<ReviewChangedEvent>(envelope.Data)!;
                    reviews.InsertRow(new BigQueryInsertRow
                {
                    { "order_id", e.OrderId.ToString() },
                    { "item_id", e.ItemId.ToString() },
                    { "rating", e.Rating },   // null = deleted (tombstone)
                    { "occurred_at", e.OccurredAt },
                });
                    break;
                }
            case SalesEventTypes.OrderStatusChanged:
                {
                    var e = JsonSerializer.Deserialize<OrderStatusChangedEvent>(envelope.Data)!;
                    orderStatus.InsertRow(new BigQueryInsertRow
                {
                    { "order_id", e.OrderId.ToString() },
                    { "status", e.Status },
                    { "occurred_at", e.OccurredAt },
                });
                    break;
                }
        }
    }
}
