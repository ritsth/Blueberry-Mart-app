namespace BlueberryMart.Api.Models.Requests;

public class GoogleSignInRequest
{
    /// <summary>The Google ID token obtained on the device from the Google Sign-In SDK.</summary>
    public string IdToken { get; set; } = null!;
}
