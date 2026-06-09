namespace BlueberryMart.Api.Models.Responses;

/// <summary>An item as shown in the back-office catalogue management table.</summary>
public class InventoryItemResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsBulkOnly { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A paginated slice of catalogue items.</summary>
public class InventoryItemPage
{
    public List<InventoryItemResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
