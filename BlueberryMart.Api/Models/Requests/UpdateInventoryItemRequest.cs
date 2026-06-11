namespace BlueberryMart.Api.Models.Requests;

/// <summary>Edit an item's details (not its stock — use the adjust endpoint for that).</summary>
public class UpdateInventoryItemRequest
{
    public string ItemName { get; set; } = null!;
    public decimal Price { get; set; }
    public bool IsBulkOnly { get; set; }
    public string? ImageUrl { get; set; }
}
