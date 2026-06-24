namespace BlueberryMart.Api.Models.Requests;

public class ResetPasswordRequest
{
    /// <summary>The auth-code row id from the reset link's <c>uid</c> query parameter.</summary>
    public Guid Uid { get; set; }

    /// <summary>The secret from the reset link's <c>t</c> query parameter.</summary>
    public string Token { get; set; } = null!;

    public string NewPassword { get; set; } = null!;
}
