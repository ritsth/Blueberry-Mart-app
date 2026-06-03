using BlueberryMart.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/shareholders")]
[Authorize(Roles = "Shareholder")]
public class ShareholderController(BlueberryMartDbContext context) : ControllerBase
{
    // GET /api/shareholders/analytics
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        var totalRevenue = await context.Orders
            .SumAsync(o => o.TotalAmount);

        var revenueByBranch = await context.Orders
            .GroupBy(o => new { o.BranchId, o.Branch.Name })
            .Select(g => new
            {
                BranchId = g.Key.BranchId,
                BranchName = g.Key.Name,
                Revenue = g.Sum(o => o.TotalAmount),
                OrderCount = g.Count()
            })
            .OrderByDescending(x => x.Revenue)
            .ToListAsync();

        var topSellingItems = await context.OrderItems
            .GroupBy(oi => new { oi.ItemId, oi.Item.ItemName, oi.Item.BranchId, oi.Item.Price })
            .Select(g => new
            {
                ItemId = g.Key.ItemId,
                ItemName = g.Key.ItemName,
                BranchId = g.Key.BranchId,
                UnitPrice = g.Key.Price,
                TotalQuantitySold = g.Sum(oi => oi.Quantity),
                TotalRevenue = g.Sum(oi => oi.Quantity * oi.UnitPrice)
            })
            .OrderByDescending(x => x.TotalQuantitySold)
            .Take(10)
            .ToListAsync();

        var lowStockAlerts = await context.Inventory
            .Where(i => i.StockQuantity < 10)
            .Select(i => new
            {
                i.Id,
                i.ItemName,
                i.BranchId,
                BranchName = i.Branch.Name,
                i.StockQuantity,
                i.IsBulkOnly
            })
            .OrderBy(i => i.StockQuantity)
            .ToListAsync();

        return Ok(new
        {
            TotalRevenue = totalRevenue,
            RevenueByBranch = revenueByBranch,
            TopSellingItems = topSellingItems,
            LowStockAlerts = lowStockAlerts
        });
    }
}
