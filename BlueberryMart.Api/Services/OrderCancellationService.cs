using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Default <see cref="IOrderCancellationService"/>. Scoped, so it shares the request's
/// <see cref="BlueberryMartDbContext"/> — the tracked <c>order</c> it's handed is saved by the same
/// context. Restores reserved stock, sets <c>cancelled</c>, stages an <c>OrderStatusChanged</c>
/// sales event in the same transaction, then publishes the per-line stock-changed events (which
/// re-trigger back-in-stock notifications) after commit.
/// </summary>
public sealed class OrderCancellationService(
    BlueberryMartDbContext context,
    IStockEventProducer stockEvents,
    ISalesEventOutbox salesEvents) : IOrderCancellationService
{
    public async Task CancelAsync(Order order, CancellationToken ct = default)
    {
        // Return the stock reserved at placement.
        var lines = await (from oi in context.OrderItems
                           join inv in context.Inventory on oi.ItemId equals inv.Id
                           where oi.OrderId == order.Id
                           select new { oi.Quantity, Inv = inv }).ToListAsync(ct);

        var events = new List<StockChangedEvent>();
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
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
            salesEvents.OrderStatusChanged(new OrderStatusChangedEvent(order.Id, "cancelled", now));

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        foreach (var evt in events) stockEvents.Publish(evt);
    }
}
