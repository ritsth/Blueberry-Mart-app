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

    /// <summary>Consumer group for the BigQuery sink (independent of the back-in-stock group).</summary>
    public string ConsumerGroup { get; set; } = "blueberrymart-bigquery-sink";
}
