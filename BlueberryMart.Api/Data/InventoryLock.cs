using BlueberryMart.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Data;

/// <summary>
/// Pessimistic row locking for inventory stock updates. Every read-modify-write on
/// <see cref="Inventory.StockQuantity"/> — order placement, in-store till sales, restock, adjust,
/// and the cancellation/expiry restock — must lock the rows it will change with
/// <c>SELECT … FOR UPDATE</c> <b>inside a transaction</b>, so two concurrent updates serialise
/// instead of both reading the same value and overwriting each other (oversell / lost restock).
///
/// Rows are locked in ascending id order so callers touching overlapping item sets can't deadlock.
/// The returned entities are tracked, so the caller mutates them and calls <c>SaveChanges</c> as
/// usual; any later query for the same ids on the same context returns these same locked instances
/// (EF identity resolution), so existing join-based reads keep working unchanged.
/// </summary>
public static class InventoryLock
{
    public static Task<List<Inventory>> ForUpdateAsync(
        BlueberryMartDbContext context, IEnumerable<Guid> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().OrderBy(x => x).ToArray();
        if (ids.Length == 0)
            return Task.FromResult(new List<Inventory>());

        // Raw SQL (not composed further) so the FOR UPDATE survives to the server. `= ANY(@ids)`
        // takes the Guid[] as a single uuid[] parameter.
        return context.Inventory
            .FromSqlInterpolated($"SELECT * FROM inventory WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(ct);
    }
}
