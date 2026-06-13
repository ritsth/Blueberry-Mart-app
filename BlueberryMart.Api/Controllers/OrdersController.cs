using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Customer,Shareholder")]
public class OrdersController(
    BlueberryMartDbContext context,
    IStockEventProducer stockEvents,
    ISalesEventOutbox salesEvents,
    IOrderCancellationService cancellation,
    ISettingsService settings) : ControllerBase
{

    // GET /api/orders/{id}
    // Lets the app read an order's current state — including payment status — after
    // the eSewa redirect, rather than trusting the deep link alone.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var order = await context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null)
            return NotFound(new { message = "Order not found." });

        var payment = await context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == id);

        return Ok(new
        {
            order.Id,
            order.OrderNumber,
            order.Status,
            order.OrderType,
            order.TotalAmount,
            order.DiscountAmount,
            order.DeliveryFee,
            order.DeliveryAddress,
            order.CreatedAt,
            Payment = payment is null
                ? null
                : (object)new { payment.Status, payment.TransactionUuid, payment.ProviderRef }
        });
    }

    // POST /api/orders/{id}/receive
    // Customer confirms they received the order; moves it from 'ready' to 'completed'
    // (the terminal state for both pickup and delivery). Only available once staff have
    // prepared the order (marked it 'ready'). Reviewing an order requires it to be completed.
    [HttpPost("{id:guid}/receive")]
    public async Task<IActionResult> MarkReceived(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var order = await context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null)
            return NotFound(new { message = "Order not found." });

        if (order.Status != "ready")
            return Conflict(new { message = "Your order isn't ready for pickup/delivery yet." });

        var now = DateTime.UtcNow;
        order.Status = "completed";
        order.UpdatedAt = now;
        salesEvents.OrderStatusChanged(new OrderStatusChangedEvent(order.Id, "completed", now));
        await context.SaveChangesAsync();

        return Ok(new { order.Id, order.Status });
    }

    // POST /api/orders/{id}/cancel
    // Customer cancels their OWN order — only while it's still 'pending' (placed but unpaid).
    // Restocks the items. A paid (confirmed) order is a refund and stays manager-only, so it's
    // rejected here. Cancellation logic is shared with the manager path (IOrderCancellationService).
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var order = await context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, ct);
        if (order is null)
            return NotFound(new { message = "Order not found." });

        if (order.Status != "pending")
            return Conflict(new
            {
                message = order.Status == "confirmed"
                    ? "This order is already paid — please contact the branch to cancel it."
                    : "This order can no longer be cancelled."
            });

        await cancellation.CancelAsync(order, ct);

        return Ok(new { order.Id, order.Status });
    }

    // POST /api/orders
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "Order must contain at least one item." });

        var config = await settings.GetAsync();
        if (config.MaintenanceMode)
            return StatusCode(503, new
            {
                message = string.IsNullOrWhiteSpace(config.MaintenanceMessage)
                    ? "Ordering is temporarily paused for maintenance. Please try again soon."
                    : config.MaintenanceMessage
            });

        var validOrderTypes = new[] { "pickup", "delivery" };
        if (!validOrderTypes.Contains(request.OrderType?.ToLower()))
            return BadRequest(new { message = "OrderType must be 'Pickup' or 'Delivery'." });

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var requestedIds = request.Items.Select(i => i.ItemId).ToList();
            var inventoryItems = await context.Inventory
                .Where(i => i.BranchId == request.BranchId && requestedIds.Contains(i.Id))
                .ToListAsync();

            // Validate all requested items exist in this branch
            var missingIds = requestedIds.Except(inventoryItems.Select(i => i.Id)).ToList();
            if (missingIds.Count > 0)
                return NotFound(new { message = "One or more items not found in the specified branch.", missingIds });

            // Validate sufficient stock for each item
            var insufficientStock = request.Items
                .Join(inventoryItems, r => r.ItemId, i => i.Id, (r, i) => new { r.Quantity, Item = i })
                .Where(x => x.Item.StockQuantity < x.Quantity)
                .Select(x => new { x.Item.Id, x.Item.ItemName, x.Item.StockQuantity })
                .ToList();

            if (insufficientStock.Count > 0)
                return Conflict(new { message = "Insufficient stock for one or more items.", insufficientStock });

            // Deduct stock and calculate the subtotal
            decimal subtotal = 0;
            var lineItems = new List<(Inventory Inv, int Qty)>();
            foreach (var requestItem in request.Items)
            {
                var inv = inventoryItems.First(i => i.Id == requestItem.ItemId);
                inv.StockQuantity -= requestItem.Quantity;
                inv.UpdatedAt = DateTime.UtcNow;
                subtotal += inv.Price * requestItem.Quantity;
                lineItems.Add((inv, requestItem.Quantity));
            }

            // Apply the 5% member discount if the user is a member
            var user = await context.Users.FindAsync(userId);
            var isMember = user is { IsMember: true };
            var discount = isMember
                ? Math.Round(subtotal * config.MemberDiscountRate, 2)
                : 0m;

            // Resolve delivery details
            var orderType = request.OrderType!.ToLower();
            string? deliveryAddressSnapshot = null;
            decimal deliveryFee = 0m;
            if (orderType == "delivery")
            {
                if (request.AddressId is null)
                    return BadRequest(new { message = "A delivery address is required for delivery orders." });

                var address = await context.Addresses
                    .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId);
                if (address is null)
                    return BadRequest(new { message = "The selected delivery address was not found." });

                deliveryAddressSnapshot = address.Phone is null
                    ? $"{address.Label}: {address.AddressLine}, {address.City}"
                    : $"{address.Label}: {address.AddressLine}, {address.City} (Phone: {address.Phone})";

                // Members get free delivery
                deliveryFee = isMember ? 0m : config.DeliveryFee;
            }

            var goodsTotal = subtotal - discount;   // loyalty points earned on this
            var total = goodsTotal + deliveryFee;

            // Create the order
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BranchId = request.BranchId,
                OrderType = orderType,
                Status = "pending",
                TotalAmount = total,
                DiscountAmount = discount,
                DeliveryAddress = deliveryAddressSnapshot,
                DeliveryFee = deliveryFee,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Orders.Add(order);

            // Persist line items for analytics, capturing each line id + position (rn) so the
            // sales event can carry them. Line 1 (rn=1) is the primary line that holds the
            // order-level discount/delivery fee in the warehouse.
            var eventLines = new List<OrderLineDto>();
            var rn = 0;
            foreach (var (inv, qty) in lineItems)
            {
                rn++;
                var lineId = Guid.NewGuid();
                context.OrderItems.Add(new OrderItem
                {
                    Id = lineId,
                    OrderId = order.Id,
                    ItemId = inv.Id,
                    Quantity = qty,
                    UnitPrice = inv.Price
                });
                eventLines.Add(new OrderLineDto(lineId, inv.Id, inv.ItemName, inv.IsBulkOnly, qty, inv.Price, rn));
            }

            // Loyalty points are credited later, when the eSewa payment completes
            // (see PaymentsController.Success) — not at placement, so unpaid orders earn nothing.

            await context.SaveChangesAsync();   // populates the DB-generated order.OrderNumber

            // Stage the OrderPlaced sales event into the outbox, in the same transaction.
            var branchName = await context.Branches
                .Where(b => b.Id == request.BranchId).Select(b => b.Name).FirstAsync();
            salesEvents.OrderPlaced(new OrderPlacedEvent(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                OccurredAt: order.CreatedAt,
                BranchName: branchName,
                OrderType: order.OrderType,
                Channel: order.Channel,
                IsMember: isMember,
                CustomerId: userId,
                OrderDiscount: order.DiscountAmount,
                OrderDeliveryFee: order.DeliveryFee,
                Lines: eventLines));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Emit a stock-change event per item now that the order is committed.
            // (Fire-and-forget; a no-op unless Kafka is configured.)
            foreach (var (inv, qty) in lineItems)
            {
                stockEvents.Publish(new StockChangedEvent(
                    ItemId: inv.Id,
                    BranchId: inv.BranchId,
                    ItemName: inv.ItemName,
                    OldQuantity: inv.StockQuantity + qty,
                    NewQuantity: inv.StockQuantity,
                    Reason: "order_placed",
                    OccurredAt: DateTime.UtcNow));
            }

            return CreatedAtAction(nameof(PlaceOrder), new { id = order.Id }, new
            {
                order.Id,
                order.OrderNumber,
                order.Status,
                Subtotal = subtotal,
                order.DiscountAmount,
                order.DeliveryFee,
                order.TotalAmount,
                order.OrderType,
                order.DeliveryAddress,
                LoyaltyPointsEarned = (int)Math.Floor(goodsTotal)
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
