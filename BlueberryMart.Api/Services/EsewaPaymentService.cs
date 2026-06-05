using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Implements eSewa's ePay-v2 flow: builds the HMAC-SHA256 signed form the app posts
/// into a webview, verifies the signed <c>data</c> payload eSewa redirects back with,
/// and confirms the transaction against eSewa's status API.
/// </summary>
public class EsewaPaymentService : IEsewaPaymentService
{
    // eSewa signs over these fields, in this order, for both request and response.
    private const string InitiationSignedFields = "total_amount,transaction_uuid,product_code";

    private readonly EsewaOptions _options;
    private readonly HttpClient _http;

    public EsewaPaymentService(IOptions<EsewaOptions> options, HttpClient http)
    {
        _options = options.Value;
        _http = http;
    }

    public EsewaFormPayload BuildInitiationPayload(Payment payment)
    {
        var total = FormatAmount(payment.Amount);
        var signature = Sign(
            $"total_amount={total},transaction_uuid={payment.TransactionUuid},product_code={_options.MerchantCode}");

        var fields = new Dictionary<string, string>
        {
            ["amount"] = total,
            ["tax_amount"] = "0",
            ["total_amount"] = total,
            ["transaction_uuid"] = payment.TransactionUuid,
            ["product_code"] = _options.MerchantCode,
            ["product_service_charge"] = "0",
            ["product_delivery_charge"] = "0",
            ["success_url"] = $"{_options.ApiBaseUrl}/api/payments/esewa/success",
            ["failure_url"] = $"{_options.ApiBaseUrl}/api/payments/esewa/failure",
            ["signed_field_names"] = InitiationSignedFields,
            ["signature"] = signature
        };

        return new EsewaFormPayload(_options.FormUrl, fields);
    }

    public string Sign(string message)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SecretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

    public EsewaCallbackResult VerifyAndDecode(string base64Data)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string Field(string name) => root.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

        var signedFieldNames = Field("signed_field_names");
        var providedSignature = Field("signature");

        // Recompute the signature over exactly the fields eSewa listed, in their order.
        var message = string.Join(",",
            signedFieldNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(name => $"{name.Trim()}={Field(name.Trim())}"));
        var expected = Sign(message);

        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(providedSignature));

        return new EsewaCallbackResult(
            SignatureValid: valid,
            TransactionUuid: Field("transaction_uuid"),
            Status: Field("status"),
            TransactionCode: Field("transaction_code") is { Length: > 0 } code ? code : null,
            TotalAmount: ParseAmount(Field("total_amount")));
    }

    public async Task<bool> ConfirmViaStatusApiAsync(string transactionUuid, decimal totalAmount, CancellationToken ct = default)
    {
        var url = $"{_options.StatusUrl}?product_code={Uri.EscapeDataString(_options.MerchantCode)}" +
                  $"&total_amount={Uri.EscapeDataString(FormatAmount(totalAmount))}" +
                  $"&transaction_uuid={Uri.EscapeDataString(transactionUuid)}";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return false;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.TryGetProperty("status", out var status)
               && string.Equals(status.GetString(), "COMPLETE", StringComparison.OrdinalIgnoreCase);
    }

    // eSewa expects plain numbers without thousands separators; keep request, signature
    // and status-check formatting identical so the signature always matches.
    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string raw) =>
        decimal.TryParse(raw.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
