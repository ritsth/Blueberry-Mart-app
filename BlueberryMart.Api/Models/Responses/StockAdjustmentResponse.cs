namespace BlueberryMart.Api.Models.Responses;

/// <summary>A stock-adjustment audit row for the item history view.</summary>
public class StockAdjustmentResponse
{
    public DateTime CreatedAt { get; set; }
    public string UserEmail { get; set; } = null!;
    public int Delta { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; } = null!;
}
