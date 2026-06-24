namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Strongly-typed settings for transactional email (Resend), bound from the <c>"Email"</c>
/// configuration section. Blank defaults ship in appsettings.json; production sets
/// <c>Email:ApiKey</c>/<c>Email:FromAddress</c>/<c>Email:PublicBaseUrl</c> via the
/// <c>EMAIL__APIKEY</c>/<c>EMAIL__FROMADDRESS</c>/<c>EMAIL__PUBLICBASEURL</c> environment variables.
///
/// <para>When <see cref="ApiKey"/> is empty the app falls back to a logging email sender (local
/// dev + tests) so nothing is actually sent — see Program.cs.</para>
/// </summary>
public class EmailOptions
{
    /// <summary>Resend API key. Empty ⇒ emails are logged instead of sent.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>From address for verification/reset mail, e.g. <c>noreply@yourdomain</c>.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Display name on the From address.</summary>
    public string FromName { get; set; } = "Blueberry Mart";

    /// <summary>
    /// Public origin of this API (no trailing slash), used to build the verification/reset link
    /// URLs that go in emails, e.g. <c>https://blueberrymart-api-….run.app</c>.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";
}
