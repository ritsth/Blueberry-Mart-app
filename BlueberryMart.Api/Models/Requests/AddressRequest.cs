namespace BlueberryMart.Api.Models.Requests;

public class AddressRequest
{
    public string Label { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string City { get; set; } = null!;
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
}
