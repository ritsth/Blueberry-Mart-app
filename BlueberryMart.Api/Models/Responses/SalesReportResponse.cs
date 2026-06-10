namespace BlueberryMart.Api.Models.Responses;

/// <summary>Aggregated sales metrics for a branch over a date range.</summary>
public class SalesReportResponse
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    /// <summary>Revenue from paid orders (confirmed/processing/ready/completed).</summary>
    public decimal TotalRevenue { get; set; }
    public int OrderCount { get; set; }
    public decimal AverageOrderValue { get; set; }
    public List<StatusCount> ByStatus { get; set; } = [];
    public List<TopItem> TopItems { get; set; } = [];
}

public class StatusCount
{
    public string Status { get; set; } = null!;
    public int Count { get; set; }
}

public class TopItem
{
    public string ItemName { get; set; } = null!;
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}
