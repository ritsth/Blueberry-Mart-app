namespace BlueberryMart.Api.Models.Requests;

public class RegisterRequest
{
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;

    /// <summary>Optional — links this sign-up to a "guest" account created at the till with the same
    /// phone, so the customer inherits the loyalty/orders they earned in store (account claim).</summary>
    public string? Phone { get; set; }
}
