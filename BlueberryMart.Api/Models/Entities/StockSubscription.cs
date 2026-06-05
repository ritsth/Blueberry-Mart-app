namespace BlueberryMart.Api.Models.Entities;

/// <summary>
/// A customer's request to be notified when a specific inventory item (an item at a
/// branch) comes back in stock. Fulfilled by the Kafka stock-event consumer when the
/// item transitions from out-of-stock to in-stock.
/// </summary>
public class StockSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The inventory row (item at a branch) being watched.</summary>
    public Guid InventoryId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the back-in-stock notification was sent; null while still waiting.</summary>
    public DateTime? NotifiedAt { get; set; }

    public User User { get; set; } = null!;
    public Inventory Item { get; set; } = null!;
}
