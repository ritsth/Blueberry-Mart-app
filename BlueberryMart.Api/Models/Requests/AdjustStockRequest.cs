namespace BlueberryMart.Api.Models.Requests;

/// <summary>
/// Change an item's stock by a signed delta (e.g. +50 restock, -3 correction).
/// The resulting quantity may not go below zero.
/// </summary>
public class AdjustStockRequest
{
    public int Delta { get; set; }
    public string? Reason { get; set; }
}
