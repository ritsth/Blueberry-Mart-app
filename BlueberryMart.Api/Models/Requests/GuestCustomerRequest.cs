namespace BlueberryMart.Api.Models.Requests;

/// <summary>Quick-create a "guest" customer at the till from just a phone number.</summary>
public class GuestCustomerRequest
{
    public string? Phone { get; set; }
}
