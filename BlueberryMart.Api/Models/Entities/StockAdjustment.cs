namespace BlueberryMart.Api.Models.Entities;

/// <summary>
/// An audit row for a manual back-office stock change: who adjusted what, by how
/// much, why, and the resulting quantity.
/// </summary>
public class StockAdjustment
{
    public Guid Id { get; set; }
    public Guid InventoryId { get; set; }
    public Guid BranchId { get; set; }
    public Guid UserId { get; set; }
    public int Delta { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public Inventory Item { get; set; } = null!;
    public User User { get; set; } = null!;
}
