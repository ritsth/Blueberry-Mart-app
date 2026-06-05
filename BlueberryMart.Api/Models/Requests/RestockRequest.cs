namespace BlueberryMart.Api.Models.Requests;

public class RestockRequest
{
    /// <summary>How many units to add to the current stock (must be positive).</summary>
    public int Quantity { get; set; }
}
