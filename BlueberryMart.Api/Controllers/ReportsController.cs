using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Branch-level sales reporting for managers and admins. Branch-scoped: managers
/// see their own branch; admins see any branch (or all branches if none given).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Manager,Admin")]
public class ReportsController(BlueberryMartDbContext context) : ControllerBase
{
    // "Paid" = an order that has been confirmed (eSewa or manual) through completion.
    private static readonly string[] PaidStatuses = ["confirmed", "processing", "ready", "completed"];

    [HttpGet("sales")]
    public async Task<ActionResult<SalesReportResponse>> Sales(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? branchId)
    {
        var isAdmin = User.IsInRole("Admin");
        Guid? mine = Guid.TryParse(User.FindFirstValue("branch"), out var b) ? b : null;
        if (!isAdmin && mine is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });

        // Treat from/to as whole UTC days: from = start-of-day inclusive,
        // to = end of that day (next day, exclusive). `.Date` drops any time part the
        // client may send, and `SpecifyKind(Utc)` lets Npgsql compare to timestamptz.
        var toInclusive = DateTime.SpecifyKind((to ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var fromDate = DateTime.SpecifyKind((from ?? toInclusive.AddDays(-30)).Date, DateTimeKind.Utc);
        var toExclusive = toInclusive.AddDays(1);

        var orders = context.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toExclusive);
        if (!isAdmin)
            orders = orders.Where(o => o.BranchId == mine!.Value);
        else if (branchId.HasValue)
            orders = orders.Where(o => o.BranchId == branchId.Value);

        var byStatus = await orders
            .GroupBy(o => o.Status)
            .Select(g => new StatusCount { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var paid = orders.Where(o => PaidStatuses.Contains(o.Status));
        var totalRevenue = await paid.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var orderCount = await paid.CountAsync();

        var paidIds = paid.Select(o => o.Id);
        var topItems = await (from oi in context.OrderItems.AsNoTracking()
                              where paidIds.Contains(oi.OrderId)
                              join inv in context.Inventory.AsNoTracking() on oi.ItemId equals inv.Id
                              group new { oi, inv } by new { inv.Id, inv.ItemName } into g
                              select new TopItem
                              {
                                  ItemName = g.Key.ItemName,
                                  QuantitySold = g.Sum(x => x.oi.Quantity),
                                  Revenue = g.Sum(x => x.oi.Quantity * x.oi.UnitPrice),
                              })
            .OrderByDescending(t => t.QuantitySold)
            .Take(5)
            .ToListAsync();

        return Ok(new SalesReportResponse
        {
            From = fromDate,
            To = toInclusive,
            TotalRevenue = totalRevenue,
            OrderCount = orderCount,
            AverageOrderValue = orderCount > 0 ? Math.Round(totalRevenue / orderCount, 2) : 0m,
            ByStatus = byStatus,
            TopItems = topItems,
        });
    }
}
