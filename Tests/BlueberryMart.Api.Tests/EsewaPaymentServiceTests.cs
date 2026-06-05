using System.Text;
using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Services;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Tests;

// Pure unit tests for the eSewa signing/verification logic — no DB or HTTP needed.
public class EsewaPaymentServiceTests
{
    private static EsewaPaymentService NewService() =>
        new(Options.Create(new EsewaOptions
        {
            SecretKey = "8gBm/:&EnhH.1/q",
            MerchantCode = "EPAYTEST",
            ApiBaseUrl = "https://api.example.com"
        }), new HttpClient());

    [Fact]
    public void Sign_MatchesEsewaDocumentedVector()
    {
        var svc = NewService();

        // eSewa's documented sandbox inputs. Expected value verified independently with:
        //   printf '%s' "total_amount=100,transaction_uuid=11-201-13,product_code=EPAYTEST" \
        //     | openssl dgst -sha256 -hmac "8gBm/:&EnhH.1/q" -binary | base64
        var signature = svc.Sign("total_amount=100,transaction_uuid=11-201-13,product_code=EPAYTEST");

        Assert.Equal("5DZywcrTKD0gia/rsSMcrRHmJl+4Tbol6S+lWgdJ94E=", signature);
    }

    [Fact]
    public void BuildInitiationPayload_SignsTotalTransactionAndProductCode()
    {
        var svc = NewService();
        var payment = new Payment { TransactionUuid = "11-201-13", Amount = 100m };

        var payload = svc.BuildInitiationPayload(payment);

        Assert.Equal("https://rc-epay.esewa.com.np/api/epay/main/v2/form", payload.FormUrl);
        Assert.Equal("100", payload.Fields["total_amount"]);
        Assert.Equal("11-201-13", payload.Fields["transaction_uuid"]);
        Assert.Equal("EPAYTEST", payload.Fields["product_code"]);
        Assert.Equal("total_amount,transaction_uuid,product_code", payload.Fields["signed_field_names"]);
        Assert.Equal("https://api.example.com/api/payments/esewa/success", payload.Fields["success_url"]);
        Assert.Equal(
            svc.Sign("total_amount=100,transaction_uuid=11-201-13,product_code=EPAYTEST"),
            payload.Fields["signature"]);
    }

    [Fact]
    public void VerifyAndDecode_AcceptsCorrectlySignedPayload()
    {
        var svc = NewService();
        var data = EncodeCallback(svc, "0KETW1234", "COMPLETE", "100", "11-201-13", "EPAYTEST");

        var result = svc.VerifyAndDecode(data);

        Assert.True(result.SignatureValid);
        Assert.Equal("COMPLETE", result.Status);
        Assert.Equal("11-201-13", result.TransactionUuid);
        Assert.Equal("0KETW1234", result.TransactionCode);
        Assert.Equal(100m, result.TotalAmount);
    }

    [Fact]
    public void VerifyAndDecode_RejectsTamperedPayload()
    {
        var svc = NewService();
        // Sign for amount 100, then tamper the amount to 1 before encoding.
        var signature = svc.Sign(
            "transaction_code=0KETW1234,status=COMPLETE,total_amount=100,transaction_uuid=11-201-13,product_code=EPAYTEST");
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["transaction_code"] = "0KETW1234",
            ["status"] = "COMPLETE",
            ["total_amount"] = "1",   // tampered
            ["transaction_uuid"] = "11-201-13",
            ["product_code"] = "EPAYTEST",
            ["signed_field_names"] = "transaction_code,status,total_amount,transaction_uuid,product_code",
            ["signature"] = signature
        });
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var result = svc.VerifyAndDecode(data);

        Assert.False(result.SignatureValid);
    }

    private static string EncodeCallback(
        EsewaPaymentService svc, string txnCode, string status, string total, string txnUuid, string productCode)
    {
        const string signedFields = "transaction_code,status,total_amount,transaction_uuid,product_code";
        var signature = svc.Sign(
            $"transaction_code={txnCode},status={status},total_amount={total},transaction_uuid={txnUuid},product_code={productCode}");
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["transaction_code"] = txnCode,
            ["status"] = status,
            ["total_amount"] = total,
            ["transaction_uuid"] = txnUuid,
            ["product_code"] = productCode,
            ["signed_field_names"] = signedFields,
            ["signature"] = signature
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
