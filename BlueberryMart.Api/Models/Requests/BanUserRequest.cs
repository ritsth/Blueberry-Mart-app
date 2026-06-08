namespace BlueberryMart.Api.Models.Requests;

public class BanUserRequest
{
    /// <summary>Optional reason recorded with the ban (shown in the admin portal).</summary>
    public string? Reason { get; set; }
}
