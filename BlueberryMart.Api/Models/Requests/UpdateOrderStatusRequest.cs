namespace BlueberryMart.Api.Models.Requests;

/// <summary>Advance an order to the next fulfillment status (confirmed→processing→ready→completed).</summary>
public class UpdateOrderStatusRequest
{
    public string Status { get; set; } = null!;
}
