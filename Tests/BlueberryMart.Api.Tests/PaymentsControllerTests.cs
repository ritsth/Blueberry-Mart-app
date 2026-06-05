using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services;
using BlueberryMart.Api.Services.Interfaces;
using BlueberryMart.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueberryMart.Api.Tests;

[Collection("Integration")]
public class PaymentsControllerTests
{
    // Sandbox secret shipped in appsettings.json and loaded in the Testing environment.
    private const string Secret = "8gBm/:&EnhH.1/q";

    private readonly BlueberryMartApiFactory _factory;
    private readonly Guid _downtownBranchId;
    private readonly Guid _eggsItemId;

    public PaymentsControllerTests(BlueberryMartApiFactory factory)
    {
        _factory          = factory;
        _downtownBranchId = factory.DowntownBranchId;
        _eggsItemId       = factory.EggsItemId;
    }

    // Client whose eSewa HttpClient is stubbed to report COMPLETE, with redirects
    // disabled so we can inspect the deep-link Location header instead of following
    // the non-http blueberrymart:// scheme.
    private HttpClient CreateClient(string statusBody = "{\"status\":\"COMPLETE\"}") =>
        _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
                s.AddHttpClient<IEsewaPaymentService, EsewaPaymentService>()
                 .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(statusBody))))
        .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task FullFlow_InitiateThenSuccess_ConfirmsOrderAndCreditsPoints()
    {
        var client = CreateClient();
        var token = await TestHelpers.GetCustomerTokenAsync(client);
        var orderId = await TestHelpers.PlaceOrderAsync(client, token, _downtownBranchId, _eggsItemId);

        // Initiate returns the signed eSewa form fields.
        var initiate = new HttpRequestMessage(HttpMethod.Post, "/api/payments/esewa/initiate")
        {
            Content = JsonContent.Create(new { orderId })
        }.WithBearer(token);
        var initiateResp = await client.SendAsync(initiate);
        Assert.Equal(HttpStatusCode.OK, initiateResp.StatusCode);

        var fields = (await initiateResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields");
        var txnUuid = fields.GetProperty("transaction_uuid").GetString()!;
        var total = fields.GetProperty("total_amount").GetString()!;

        var (pointsBefore, _) = await ReadUserPointsAndOrderStatus(orderId);

        // eSewa redirects the webview to /success with a signed data payload.
        var data = EncodeSuccessPayload("THECODE7", total, txnUuid);
        var successResp = await client.GetAsync($"/api/payments/esewa/success?data={Uri.EscapeDataString(data)}");

        Assert.Equal(HttpStatusCode.Redirect, successResp.StatusCode);
        Assert.StartsWith("blueberrymart://payment/success", successResp.Headers.Location!.ToString());
        Assert.Contains(orderId.ToString(), successResp.Headers.Location!.ToString());

        // Server recorded the truth: payment completed, order confirmed, points credited.
        var (pointsAfter, orderStatus) = await ReadUserPointsAndOrderStatus(orderId);
        Assert.Equal("confirmed", orderStatus);
        Assert.True(pointsAfter > pointsBefore);

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var payment = await ctx.Payments.AsNoTracking().FirstAsync(p => p.OrderId == orderId);
        Assert.Equal("completed", payment.Status);
        Assert.Equal("THECODE7", payment.ProviderRef);

        // The read-back endpoint the app polls after the redirect reflects the same truth.
        var readBack = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}").WithBearer(token));
        var orderJson = await readBack.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("confirmed", orderJson.GetProperty("status").GetString());
        Assert.Equal("completed", orderJson.GetProperty("payment").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Success_TamperedSignature_RedirectsToFailureAndLeavesOrderPending()
    {
        var client = CreateClient();
        var token = await TestHelpers.GetCustomerTokenAsync(client);
        var orderId = await TestHelpers.PlaceOrderAsync(client, token, _downtownBranchId, _eggsItemId);

        var initiate = new HttpRequestMessage(HttpMethod.Post, "/api/payments/esewa/initiate")
        {
            Content = JsonContent.Create(new { orderId })
        }.WithBearer(token);
        var fields = (await (await client.SendAsync(initiate)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fields");
        var txnUuid = fields.GetProperty("transaction_uuid").GetString()!;
        var total = fields.GetProperty("total_amount").GetString()!;

        // Sign for `total`, then tamper the amount so the signature no longer matches.
        var signed = SignSuccess("THECODE7", total, txnUuid);
        var tampered = BuildPayload("THECODE7", "1", txnUuid, signed);
        var resp = await client.GetAsync($"/api/payments/esewa/success?data={Uri.EscapeDataString(tampered)}");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.StartsWith("blueberrymart://payment/failure", resp.Headers.Location!.ToString());

        var (_, orderStatus) = await ReadUserPointsAndOrderStatus(orderId);
        Assert.Equal("pending", orderStatus);
    }

    [Fact]
    public async Task Initiate_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/payments/esewa/initiate", new { orderId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private async Task<(int points, string orderStatus)> ReadUserPointsAndOrderStatus(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();
        var order = await ctx.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        var user = await ctx.Users.AsNoTracking().FirstAsync(u => u.Id == order.UserId);
        return (user.LoyaltyPoints, order.Status);
    }

    private static string EncodeSuccessPayload(string txnCode, string total, string txnUuid) =>
        BuildPayload(txnCode, total, txnUuid, SignSuccess(txnCode, total, txnUuid));

    private static string SignSuccess(string txnCode, string total, string txnUuid)
    {
        var message =
            $"transaction_code={txnCode},status=COMPLETE,total_amount={total},transaction_uuid={txnUuid},product_code=EPAYTEST";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    private static string BuildPayload(string txnCode, string total, string txnUuid, string signature)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["transaction_code"] = txnCode,
            ["status"] = "COMPLETE",
            ["total_amount"] = total,
            ["transaction_uuid"] = txnUuid,
            ["product_code"] = "EPAYTEST",
            ["signed_field_names"] = "transaction_code,status,total_amount,transaction_uuid,product_code",
            ["signature"] = signature
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    // Returns a canned eSewa status-API response so /success can confirm offline.
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
