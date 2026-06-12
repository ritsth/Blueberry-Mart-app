using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Releases stock reserved by unpaid orders. An order deducts stock at placement; if the
/// customer never pays, this returns that stock after a hold window so it isn't locked
/// forever. The restock emits stock-change events, which can re-trigger back-in-stock
/// notifications for items that were sold out.
/// </summary>
public sealed class OrderExpiryService(
    BlueberryMartDbContext db,
    IStockEventProducer stockEvents,
    ISalesEventOutbox salesEvents,
    ILogger<OrderExpiryService> logger) : IOrderExpiryService
{
    public async Task<int> SweepExpiredAsync(TimeSpan holdWindow, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - holdWindow;

        var expiredIds = await db.Orders
            .Where(o => o.Status == "pending" && o.CreatedAt < cutoff
                        && !db.Payments.Any(p => p.OrderId == o.Id && p.Status == "completed"))
            .Select(o => o.Id)
            .Take(200)   // bound each sweep
            .ToListAsync(ct);

        var count = 0;
        foreach (var id in expiredIds)
        {
            if (await ExpireOneAsync(id, ct))
                count++;
        }

        if (count > 0)
            logger.LogInformation("Expired {Count} unpaid order(s) past the {Mins}-minute hold",
                count, (int)holdWindow.TotalMinutes);
        return count;
    }

    private async Task<bool> ExpireOneAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.Status != "pending")
            return false;   // re-check under the loop — idempotent against races

        var lines = await (from oi in db.OrderItems
                           join inv in db.Inventory on oi.ItemId equals inv.Id
                           where oi.OrderId == orderId
                           select new { oi.Quantity, Inv = inv }).ToListAsync(ct);

        var events = new List<StockChangedEvent>();
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
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
                    Reason: "order_expired",
                    OccurredAt: now));
            }

            order.Status = "cancelled";
            order.UpdatedAt = now;
            salesEvents.OrderStatusChanged(new OrderStatusChangedEvent(order.Id, "cancelled", now));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // After commit: feed the stream (back-in-stock notifications + analytics).
        foreach (var evt in events)
            stockEvents.Publish(evt);

        return true;
    }
}
