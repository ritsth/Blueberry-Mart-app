namespace BlueberryMart.Api.Models.Requests;

/// <summary>Link a phone to the signed-in account (and claim a matching till "guest").</summary>
public class LinkPhoneRequest
{
    public string? Phone { get; set; }
}
