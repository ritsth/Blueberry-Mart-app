namespace BlueberryMart.Api.Models.Responses;

/// <summary>At-a-glance counts for the back-office dashboard, scoped to the caller's branch.</summary>
public class DashboardSummary
{
    /// <summary>Active items at or below the low-stock threshold.</summary>
    public int LowStockItems { get; set; }
    /// <summary>Orders awaiting payment.</summary>
    public int PendingOrders { get; set; }
    /// <summary>Orders in fulfillment (confirmed/processing/ready).</summary>
    public int ActiveOrders { get; set; }
}
