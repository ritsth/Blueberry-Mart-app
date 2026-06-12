using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Models.Responses;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Back-office order fulfillment for store staff. Branch-scoped like inventory
/// management: staff/managers act only on their own branch's orders; admins on any.
/// Cancellation is manager/admin only.
/// </summary>
[ApiController]
[Route("api/orders/manage")]
[Authorize(Roles = "Staff,Manager,Admin")]
public class ManageOrdersController(BlueberryMartDbContext context, IStockEventProducer stockEvents, ISalesEventOutbox salesEvents) : ControllerBase
{
    // Linear forward fulfillment chain (paid orders only — pending is advanced by
    // recording a payment, not here). Cancellation has its own manager-only endpoint.
    private static readonly Dictionary<string, string> NextStatus = new()
    {
        ["confirmed"] = "processing",
        ["processing"] = "ready",
        ["ready"] = "completed",
    };

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid? CallerBranch() =>
        Guid.TryParse(User.FindFirstValue("branch"), out var b) ? b : null;

    private ActionResult? GuardBranch(Guid branchId)
    {
        if (IsAdmin) return null;
        var mine = CallerBranch();
        if (mine is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });
        if (mine.Value != branchId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only manage orders in your own branch." });
        return null;
    }

    [HttpGet]
    public async Task<ActionResult<ManagedOrderPage>> List(
        [FromQuery] Guid? branchId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = context.Orders.AsNoTracking().AsQueryable();

        if (!IsAdmin)
        {
            var mine = CallerBranch();
            if (mine is null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });
            query = query.Where(o => o.BranchId == mine.Value);
        }
        else if (branchId.HasValue)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status.ToLower());
        if (!string.IsNullOrWhiteSpace(search) && int.TryParse(search.Trim().TrimStart('#'), out var num))
            query = query.Where(o => o.OrderNumber == num);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new ManagedOrderResponse
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerEmail = o.User.Email,
                BranchId = o.BranchId,
                BranchName = o.Branch.Name,
                OrderType = o.OrderType,
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                PaymentStatus = context.Payments.Where(p => p.OrderId == o.Id).Select(p => p.Status).FirstOrDefault() ?? "unpaid",
                CreatedAt = o.CreatedAt,
            })
            .ToListAsync();

        return Ok(new ManagedOrderPage { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ManagedOrderDetailResponse>> Get(Guid id)
    {
        var order = await context.Orders.AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Branch)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        var payment = await context.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == id);
        var lines = await (from oi in context.OrderItems.AsNoTracking()
                           join inv in context.Inventory.AsNoTracking() on oi.ItemId equals inv.Id
                           where oi.OrderId == id
                           select new ManagedOrderLineItem
                           {
                               ItemName = inv.ItemName,
                               Quantity = oi.Quantity,
                               UnitPrice = oi.UnitPrice,
                           }).ToListAsync();

        return Ok(new ManagedOrderDetailResponse
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerEmail = order.User.Email,
            BranchId = order.BranchId,
            BranchName = order.Branch.Name,
            OrderType = order.OrderType,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            DiscountAmount = order.DiscountAmount,
            DeliveryFee = order.DeliveryFee,
            DeliveryAddress = order.DeliveryAddress,
            PaymentStatus = payment?.Status ?? "unpaid",
            PaymentRef = payment?.ProviderRef,
            CreatedAt = order.CreatedAt,
            Items = lines,
        });
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> AdvanceStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        var target = request.Status?.Trim().ToLower() ?? "";
        if (!NextStatus.TryGetValue(order.Status, out var allowedNext))
            return Conflict(new { message = $"An order that is '{order.Status}' can't be advanced here." });
        if (target != allowedNext)
            return BadRequest(new { message = $"From '{order.Status}' the only next status is '{allowedNext}'." });

        order.Status = target;
        order.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return Ok(new { order.Id, order.Status });
    }

    [HttpPost("{id:guid}/record-payment")]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentRequest request)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        if (order.Status != "pending")
            return Conflict(new { message = $"Order is '{order.Status}'; only a pending order can be marked paid." });

        var payment = await context.Payments.FirstOrDefaultAsync(p => p.OrderId == id);
        if (payment is { Status: "completed" })
            return Conflict(new { message = "Order is already paid." });

        var method = string.IsNullOrWhiteSpace(request.Method) ? "cash" : request.Method.Trim().ToLower();
        var now = DateTime.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            if (payment is null)
            {
                payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    TransactionUuid = $"manual-{Guid.NewGuid()}",
                    Amount = order.TotalAmount,
                    Status = "completed",
                    ProviderRef = $"manual:{method}",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.Payments.Add(payment);
            }
            else
            {
                payment.Status = "completed";
                payment.Amount = order.TotalAmount;
                payment.ProviderRef = $"manual:{method}";
                payment.UpdatedAt = now;
            }

            order.Status = "confirmed";
            order.UpdatedAt = now;

            // Mirror eSewa success: credit loyalty once paid (goods value, excl delivery).
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

        return Ok(new { order.Id, order.Status, PaymentStatus = "completed" });
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound(new { message = "Order not found." });
        if (GuardBranch(order.BranchId) is { } denied) return denied;

        if (order.Status is "completed" or "cancelled")
            return Conflict(new { message = $"A '{order.Status}' order can't be cancelled." });

        // Return the stock reserved at placement.
        var lines = await (from oi in context.OrderItems
                           join inv in context.Inventory on oi.ItemId equals inv.Id
                           where oi.OrderId == id
                           select new { oi.Quantity, Inv = inv }).ToListAsync();

        var events = new List<StockChangedEvent>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            foreach (var line in lines)
            {
                var oldQty = line.Inv.StockQuantity;
                line.Inv.StockQuantity += line.Quantity;
                line.Inv.UpdatedAt = now;
                events.Add(new StockChangedEvent(
                    ItemId: line.Inv.Id,
                    BranchId: line.Inv.BranchId,
                    ItemName: line.Inv.ItemName,
                    OldQuantity: oldQty,
                    NewQuantity: line.Inv.StockQuantity,
                    Reason: "order_cancelled",
                    OccurredAt: now));
            }

            order.Status = "cancelled";
            order.UpdatedAt = now;

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        foreach (var evt in events) stockEvents.Publish(evt);

        return Ok(new { order.Id, order.Status });
    }
}
