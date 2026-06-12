namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Settings for streaming inventory events into BigQuery, bound from the
/// <c>"BigQuery"</c> section. Empty <see cref="ProjectId"/> ⇒ BigQuery disabled
/// (the sink isn't registered and analytics report "not configured").
/// </summary>
public class BigQueryOptions
{
    public string? ProjectId { get; set; }
    public string DatasetId { get; set; } = "blueberrymart";
    public string TableId { get; set; } = "stock_events";

    /// <summary>
    /// Wide, denormalized one-row-per-order-line view that backs the self-service "Explore"
    /// analytics. Now a VIEW over the append-only raw tables below (fed by the sales event
    /// pipeline); previously a table rebuilt hourly from Postgres.
    /// </summary>
    public string SalesFactTableId { get; set; } = "sales_fact";

    /// <summary>Append-only raw table: one immutable row per order line (from OrderPlaced events).</summary>
    public string OrderLinesTableId { get; set; } = "sales_order_lines";

    /// <summary>Append-only raw table: one row per payment status change.</summary>
    public string PaymentStatusTableId { get; set; } = "sales_payment_status";

    /// <summary>Append-only raw table: one row per review submit/delete (rating null = deleted).</summary>
    public string ReviewsTableId { get; set; } = "sales_reviews";

    /// <summary>Consumer group for the BigQuery sink (independent of the back-in-stock group).</summary>
    public string ConsumerGroup { get; set; } = "blueberrymart-bigquery-sink";
}
