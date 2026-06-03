namespace BlueberryMart.Api.Models.Entities;

public class Inventory
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string ItemName { get; set; } = null!;
    public int StockQuantity { get; set; }
    public decimal Price { get; set; }
    public bool IsBulkOnly { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
}
