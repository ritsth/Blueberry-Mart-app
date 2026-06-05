namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Strongly-typed settings for the eSewa ePay-v2 integration, bound from the
/// <c>"Esewa"</c> configuration section. Sandbox defaults ship in appsettings.json;
/// production overrides <c>Esewa:SecretKey</c>/<c>Esewa:MerchantCode</c> via the
/// <c>ESEWA__SECRETKEY</c>/<c>ESEWA__MERCHANTCODE</c> environment variables.
/// </summary>
public class EsewaOptions
{
    /// <summary>eSewa form endpoint the app POSTs the signed payload to.</summary>
    public string FormUrl { get; set; } = "https://rc-epay.esewa.com.np/api/epay/main/v2/form";

    /// <summary>Transaction status-check endpoint used to confirm a payment.</summary>
    public string StatusUrl { get; set; } = "https://rc.esewa.com.np/api/epay/transaction/status/";

    /// <summary>eSewa merchant code (<c>EPAYTEST</c> in the sandbox).</summary>
    public string MerchantCode { get; set; } = "EPAYTEST";

    /// <summary>HMAC-SHA256 signing key issued by eSewa.</summary>
    public string SecretKey { get; set; } = "8gBm/:&EnhH.1/q";

    /// <summary>Public base URL of this API, used to build the success/failure URLs eSewa redirects to.</summary>
    public string ApiBaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>App deep link to redirect to after a verified successful payment.</summary>
    public string SuccessDeepLink { get; set; } = "blueberrymart://payment/success";

    /// <summary>App deep link to redirect to after a failed/cancelled payment.</summary>
    public string FailureDeepLink { get; set; } = "blueberrymart://payment/failure";
}
