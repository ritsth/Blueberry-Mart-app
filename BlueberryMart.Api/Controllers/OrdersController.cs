using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Customer,Shareholder")]
public class OrdersController(BlueberryMartDbContext context) : ControllerBase
{
    public const decimal DeliveryFee = 100m; // flat delivery fee, waived for members

    // POST /api/orders
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "Order must contain at least one item." });

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
                ? Math.Round(subtotal * MembershipController.MemberDiscountRate, 2)
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
                deliveryFee = isMember ? 0m : DeliveryFee;
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

            // Persist line items for analytics
            foreach (var (inv, qty) in lineItems)
            {
                context.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = inv.Id,
                    Quantity = qty,
                    UnitPrice = inv.Price
                });
            }

            // Credit loyalty points: 1 point per whole unit of goods value (excludes delivery fee)
            if (user is not null)
            {
                user.LoyaltyPoints += (int)Math.Floor(goodsTotal);
                user.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

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
