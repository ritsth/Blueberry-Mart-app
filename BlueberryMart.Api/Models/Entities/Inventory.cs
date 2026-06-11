namespace BlueberryMart.Api.Models.Entities;

public class Inventory
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string ItemName { get; set; } = null!;
    public int StockQuantity { get; set; }
    public decimal Price { get; set; }
    public bool IsBulkOnly { get; set; }

    /// <summary>Public URL of the item photo (GCS or local), or null for the placeholder.</summary>
    public string? ImageUrl { get; set; }

    // Soft-delete: deactivated items are hidden from customers but kept because
    // orders reference them (FK Restrict). Managers/admins can deactivate/restore.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
}
