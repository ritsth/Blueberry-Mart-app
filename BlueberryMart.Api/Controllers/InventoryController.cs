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
[Route("api/inventory")]
public class InventoryController(BlueberryMartDbContext context, IStockEventProducer stockEvents) : ControllerBase
{
    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("customer")]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetForCustomer(
        [FromQuery] Guid branchId, [FromQuery] bool includeOutOfStock = false)
    {
        var query = context.Inventory.Where(i => i.BranchId == branchId && i.IsActive && !i.IsBulkOnly);
        if (!includeOutOfStock)   // default keeps sold-out items hidden
            query = query.Where(i => i.StockQuantity > 0);

        var items = await query.OrderByDescending(i => i.StockQuantity > 0).ThenBy(i => i.ItemName).ToListAsync();
        return Ok(items);
    }

    // Bulk (business) catalogue — Blueberry Plus members only
    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("bulk")]
    public async Task<IActionResult> GetBulk([FromQuery] Guid branchId, [FromQuery] bool includeOutOfStock = false)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null || !user.IsMember)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Bulk ordering is available to Blueberry Plus members only." });

        var query = context.Inventory.Where(i => i.BranchId == branchId && i.IsActive && i.IsBulkOnly);
        if (!includeOutOfStock)   // default keeps sold-out items hidden
            query = query.Where(i => i.StockQuantity > 0);

        var items = await query.OrderByDescending(i => i.StockQuantity > 0).ThenBy(i => i.ItemName).ToListAsync();

        return Ok(items);
    }

    // GET /api/inventory/top?branchId=&bulk=&limit=
    // A branch's best sellers: items ranked by units sold across that branch's non-cancelled
    // orders, limited to active, in-stock items in the requested catalogue (retail or bulk).
    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("top")]
    public async Task<IActionResult> Top([FromQuery] Guid branchId, [FromQuery] bool bulk = false, [FromQuery] int limit = 10)
    {
        if (bulk)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await context.Users.FindAsync(userId);
            if (user is null || !user.IsMember)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Bulk ordering is available to Blueberry Plus members only." });
        }

        limit = Math.Clamp(limit, 1, 20);

        var top = await (
            from oi in context.OrderItems
            join o in context.Orders on oi.OrderId equals o.Id
            join inv in context.Inventory on oi.ItemId equals inv.Id
            where o.BranchId == branchId && o.Status != "cancelled"
                  && inv.IsActive && inv.StockQuantity > 0 && inv.IsBulkOnly == bulk
            group oi by new { inv.Id, inv.ItemName, inv.Price, inv.StockQuantity, inv.ImageUrl } into g
            orderby g.Sum(x => x.Quantity) descending
            select new
            {
                Id = g.Key.Id,
                ItemName = g.Key.ItemName,
                Price = g.Key.Price,
                StockQuantity = g.Key.StockQuantity,
                ImageUrl = g.Key.ImageUrl,
            }).Take(limit).ToListAsync();

        return Ok(top);
    }

    // GET /api/inventory/reorder?branchId=&bulk=&limit=
    // "Buy again": items the signed-in customer has previously *paid for* at this branch (an
    // order with a completed payment, not cancelled — so refunds don't count), most-recently-
    // ordered first, limited to active in-stock items in the requested catalogue (retail or bulk).
    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("reorder")]
    public async Task<IActionResult> Reorder([FromQuery] Guid branchId, [FromQuery] bool bulk = false, [FromQuery] int limit = 10)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (bulk)
        {
            var user = await context.Users.FindAsync(userId);
            if (user is null || !user.IsMember)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Bulk ordering is available to Blueberry Plus members only." });
        }

        limit = Math.Clamp(limit, 1, 20);

        var items = await (
            from oi in context.OrderItems
            join o in context.Orders on oi.OrderId equals o.Id
            join inv in context.Inventory on oi.ItemId equals inv.Id
            where o.UserId == userId && o.BranchId == branchId && o.Status != "cancelled"
                  && context.Payments.Any(p => p.OrderId == o.Id && p.Status == "completed")
                  && inv.IsActive && inv.StockQuantity > 0 && inv.IsBulkOnly == bulk
            group o by new { inv.Id, inv.ItemName, inv.Price, inv.StockQuantity, inv.ImageUrl } into g
            orderby g.Max(x => x.CreatedAt) descending
            select new
            {
                Id = g.Key.Id,
                ItemName = g.Key.ItemName,
                Price = g.Key.Price,
                StockQuantity = g.Key.StockQuantity,
                ImageUrl = g.Key.ImageUrl,
            }).Take(limit).ToListAsync();

        return Ok(items);
    }

    [Authorize(Roles = "Shareholder")]
    [HttpGet("shareholder")]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetForShareholder()
    {
        var items = await context.Inventory
            .Include(i => i.Branch)
            .ToListAsync();

        return Ok(items);
    }

    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] bool bulk = false)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());

        var term = q.Trim();

        // bulk=false → regular (non-bulk) catalogue; bulk=true → bulk-only items.
        // Sold-out items are included so they can be searched and offer a "notify me";
        // in-stock items sort first within each branch group.
        var matches = await context.Inventory
            .Where(i => i.IsActive && i.IsBulkOnly == bulk &&
                        EF.Functions.ILike(i.ItemName, $"%{term}%"))
            .Include(i => i.Branch)
            .OrderBy(i => i.Branch.Name)
            .ThenByDescending(i => i.StockQuantity > 0)
            .ThenBy(i => i.ItemName)
            .Select(i => new
            {
                Id = i.Id,
                ItemName = i.ItemName,
                Price = i.Price,
                StockQuantity = i.StockQuantity,
                ImageUrl = i.ImageUrl,
                BranchId = i.BranchId,
                BranchName = i.Branch.Name,
                BranchCity = i.Branch.LocationCity,
            })
            .ToListAsync();

        var grouped = matches
            .GroupBy(i => new { i.BranchId, i.BranchName, i.BranchCity })
            .Select(g => new
            {
                BranchId = g.Key.BranchId,
                BranchName = g.Key.BranchName,
                BranchCity = g.Key.BranchCity,
                Items = g.Select(i => new { i.Id, i.ItemName, i.Price, i.StockQuantity, i.ImageUrl }).ToList(),
            });

        return Ok(grouped);
    }

    // POST /api/inventory/{id}/restock
    // Shareholder adds stock. Emits a stock-changed event (reason "restock"), which
    // the Kafka consumer turns into back-in-stock notifications when it crosses 0.
    [Authorize(Roles = "Shareholder")]
    [HttpPost("{id:guid}/restock")]
    public async Task<IActionResult> Restock(Guid id, [FromBody] RestockRequest request)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be positive." });

        var item = await context.Inventory.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null)
            return NotFound(new { message = "Item not found." });

        var oldQty = item.StockQuantity;
        item.StockQuantity += request.Quantity;
        item.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        stockEvents.Publish(new StockChangedEvent(
            ItemId: item.Id,
            BranchId: item.BranchId,
            ItemName: item.ItemName,
            OldQuantity: oldQty,
            NewQuantity: item.StockQuantity,
            Reason: "restock",
            OccurredAt: DateTime.UtcNow));

        return Ok(new { item.Id, item.ItemName, item.StockQuantity });
    }

    // POST /api/inventory/{id}/notify-me
    // Customer subscribes to a back-in-stock notification for an out-of-stock item.
    [Authorize(Roles = "Customer,Shareholder")]
    [HttpPost("{id:guid}/notify-me")]
    public async Task<IActionResult> NotifyMe(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var item = await context.Inventory.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null)
            return NotFound(new { message = "Item not found." });

        if (item.StockQuantity > 0)
            return Conflict(new { message = "This item is already in stock." });

        // Idempotent: one active subscription per user+item.
        var existing = await context.StockSubscriptions
            .AnyAsync(s => s.UserId == userId && s.InventoryId == id && s.NotifiedAt == null);
        if (!existing)
        {
            context.StockSubscriptions.Add(new StockSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InventoryId = id,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        return Ok(new { message = $"We'll notify you when {item.ItemName} is back in stock." });
    }
}
