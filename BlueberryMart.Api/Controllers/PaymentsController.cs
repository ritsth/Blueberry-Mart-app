using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/payments/esewa")]
public class PaymentsController(
    BlueberryMartDbContext context,
    IEsewaPaymentService esewa,
    ISalesEventOutbox salesEvents,
    IOptions<EsewaOptions> options) : ControllerBase
{
    private readonly EsewaOptions _options = options.Value;

    // POST /api/payments/esewa/initiate
    // The app submits the returned fields to formUrl inside a webview.
    [HttpPost("initiate")]
    [Authorize(Roles = "Customer,Shareholder")]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var order = await context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == userId);
        if (order is null)
            return NotFound(new { message = "Order not found." });

        if (order.Status != "pending")
            return Conflict(new { message = $"Order is '{order.Status}' and cannot be paid." });

        var payment = await context.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id);
        if (payment is { Status: "completed" })
            return Conflict(new { message = "Order is already paid." });

        if (payment is null)
        {
            payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                TransactionUuid = Guid.NewGuid().ToString(),
                Amount = order.TotalAmount,
                Status = "initiated",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Payments.Add(payment);
        }
        else
        {
            // Re-initiating a failed/abandoned attempt: fresh reference, reset status.
            payment.TransactionUuid = Guid.NewGuid().ToString();
            payment.Amount = order.TotalAmount;
            payment.Status = "initiated";
            payment.ProviderRef = null;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        salesEvents.PaymentStatusChanged(new PaymentStatusChangedEvent(order.Id, "initiated", DateTime.UtcNow));
        await context.SaveChangesAsync();

        var form = esewa.BuildInitiationPayload(payment);
        return Ok(new { formUrl = form.FormUrl, fields = form.Fields });
    }

    // GET /api/payments/esewa/success?data=<base64>
    // eSewa redirects the webview here after a successful payment.
    [HttpGet("success")]
    [AllowAnonymous]
    public async Task<IActionResult> Success([FromQuery] string? data)
    {
        if (string.IsNullOrEmpty(data))
            return RedirectToDeepLink(failure: true);

        EsewaCallbackResult result;
        try
        {
            result = esewa.VerifyAndDecode(data);
        }
        catch (FormatException)
        {
            return RedirectToDeepLink(failure: true);
        }

        if (!result.SignatureValid || !string.Equals(result.Status, "COMPLETE", StringComparison.OrdinalIgnoreCase))
            return RedirectToDeepLink(failure: true);

        var payment = await context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.TransactionUuid == result.TransactionUuid);
        if (payment is null)
            return RedirectToDeepLink(failure: true);

        // Already processed — return success idempotently.
        if (payment.Status == "completed")
            return RedirectToDeepLink(failure: false, orderId: payment.OrderId);

        // Defence in depth: confirm with eSewa's status API before crediting anything.
        var confirmed = await esewa.ConfirmViaStatusApiAsync(payment.TransactionUuid, payment.Amount);
        if (!confirmed)
            return RedirectToDeepLink(failure: true);

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            payment.Status = "completed";
            payment.ProviderRef = result.TransactionCode;
            payment.UpdatedAt = now;

            var order = payment.Order;
            order.Status = "confirmed";
            order.UpdatedAt = now;

            // Loyalty points are credited only once payment completes: 1 point per
            // whole unit of goods value (delivery fee excluded).
            var goodsTotal = order.TotalAmount - order.DeliveryFee;
            var user = await context.Users.FindAsync(order.UserId);
            if (user is not null)
            {
                user.LoyaltyPoints += (int)Math.Floor(goodsTotal);
                user.UpdatedAt = now;
            }

            salesEvents.PaymentStatusChanged(new PaymentStatusChangedEvent(order.Id, "completed", now));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return RedirectToDeepLink(failure: false, orderId: payment.OrderId);
    }

    // GET /api/payments/esewa/failure?data=<base64>
    // eSewa redirects here when a payment fails or is cancelled.
    [HttpGet("failure")]
    [AllowAnonymous]
    public async Task<IActionResult> Failure([FromQuery] string? data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            try
            {
                var result = esewa.VerifyAndDecode(data);
                if (result.SignatureValid)
                {
                    var payment = await context.Payments
                        .FirstOrDefaultAsync(p => p.TransactionUuid == result.TransactionUuid && p.Status == "initiated");
                    if (payment is not null)
                    {
                        payment.Status = "failed";
                        payment.UpdatedAt = DateTime.UtcNow;
                        salesEvents.PaymentStatusChanged(new PaymentStatusChangedEvent(payment.OrderId, "failed", DateTime.UtcNow));
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (FormatException)
            {
                // Malformed payload — fall through to the failure redirect.
            }
        }

        return RedirectToDeepLink(failure: true);
    }

    private RedirectResult RedirectToDeepLink(bool failure, Guid? orderId = null)
    {
        var target = failure ? _options.FailureDeepLink : _options.SuccessDeepLink;
        if (orderId is not null)
            target += (target.Contains('?') ? "&" : "?") + $"orderId={orderId}";
        return Redirect(target);
    }
}
