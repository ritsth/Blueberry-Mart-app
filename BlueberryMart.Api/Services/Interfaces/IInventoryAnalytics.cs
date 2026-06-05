namespace BlueberryMart.Api.Services.Interfaces;

public record StockMovementRow(string Reason, long Events, long NetChange);

/// <summary>Analytics over the inventory event history (backed by BigQuery).</summary>
public interface IInventoryAnalytics
{
    /// <summary>False when BigQuery isn't configured (e.g. production today).</summary>
    bool Enabled { get; }

    /// <summary>Event count and net stock change grouped by reason.</summary>
    Task<IReadOnlyList<StockMovementRow>> StockMovementByReasonAsync(CancellationToken ct = default);
}
