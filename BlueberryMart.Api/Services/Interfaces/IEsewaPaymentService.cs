using BlueberryMart.Api.Models.Entities;

namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>The signed form an app posts into a webview to start an eSewa payment.</summary>
public record EsewaFormPayload(string FormUrl, IReadOnlyDictionary<string, string> Fields);

/// <summary>The decoded, signature-verified result eSewa redirects back with.</summary>
public record EsewaCallbackResult(
    bool SignatureValid,
    string TransactionUuid,
    string Status,
    string? TransactionCode,
    decimal TotalAmount);

public interface IEsewaPaymentService
{
    /// <summary>Builds the signed field set the app submits to <see cref="EsewaFormPayload.FormUrl"/>.</summary>
    EsewaFormPayload BuildInitiationPayload(Payment payment);

    /// <summary>Base64(HMAC-SHA256(secret, <paramref name="message"/>)).</summary>
    string Sign(string message);

    /// <summary>Decodes the base64 <c>data</c> callback param and verifies its signature.</summary>
    EsewaCallbackResult VerifyAndDecode(string base64Data);

    /// <summary>Calls eSewa's status API and returns true only when the payment is COMPLETE.</summary>
    Task<bool> ConfirmViaStatusApiAsync(string transactionUuid, decimal totalAmount, CancellationToken ct = default);
}
