namespace BlueberryMart.Api.Models.Events;

/// <summary>
/// Emitted whenever an inventory item's stock level changes (an order is placed,
/// a restock happens, …). This is the unit of data that flows through Kafka and
/// will later feed the availability read-model and BigQuery analytics.
/// </summary>
public record StockChangedEvent(
    Guid ItemId,
    Guid BranchId,
    string ItemName,
    int OldQuantity,
    int NewQuantity,
    string Reason,          // e.g. "order_placed", "restock"
    DateTime OccurredAt);
