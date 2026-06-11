namespace BlueberryMart.Api.Models.Requests;

/// <summary>Create a catalogue item in a branch (staff/manager/admin).</summary>
public class CreateInventoryItemRequest
{
    public Guid BranchId { get; set; }
    public string ItemName { get; set; } = null!;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsBulkOnly { get; set; }
    public string? ImageUrl { get; set; }
}
