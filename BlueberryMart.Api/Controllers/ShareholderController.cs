using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/shareholders")]
[Authorize(Roles = "Shareholder")]
public class ShareholderController(BlueberryMartDbContext context, IInventoryAnalytics inventoryAnalytics) : ControllerBase
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

        // Revenue over the last 14 days, grouped by day
        var sinceDate = DateTime.UtcNow.Date.AddDays(-13);
        var dailyRaw = await context.Orders
            .Where(o => o.CreatedAt >= sinceDate)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Revenue = g.Sum(o => o.TotalAmount) })
            .ToListAsync();

        // Fill missing days with 0 so the line chart is continuous
        var revenueOverTime = Enumerable.Range(0, 14)
            .Select(offset => sinceDate.AddDays(offset))
            .Select(day => new
            {
                Date = day,
                Revenue = dailyRaw.FirstOrDefault(d => d.Day == day)?.Revenue ?? 0m
            })
            .ToList();

        // Pickup vs delivery split
        var orderTypeSplit = await context.Orders
            .GroupBy(o => o.OrderType)
            .Select(g => new { Type = g.Key, Count = g.Count(), Revenue = g.Sum(o => o.TotalAmount) })
            .ToListAsync();

        return Ok(new
        {
            TotalRevenue = totalRevenue,
            RevenueByBranch = revenueByBranch,
            TopSellingItems = topSellingItems,
            LowStockAlerts = lowStockAlerts,
            RevenueOverTime = revenueOverTime,
            OrderTypeSplit = orderTypeSplit
        });
    }

    // GET /api/shareholders/inventory-analytics
    // Stock movement from the BigQuery event warehouse. Reports enabled=false when
    // BigQuery isn't configured (e.g. production today).
    [HttpGet("inventory-analytics")]
    public async Task<IActionResult> GetInventoryAnalytics(CancellationToken ct)
    {
        if (!inventoryAnalytics.Enabled)
            return Ok(new { enabled = false, stockMovementByReason = Array.Empty<object>() });

        var movement = await inventoryAnalytics.StockMovementByReasonAsync(ct);
        return Ok(new { enabled = true, stockMovementByReason = movement });
    }
}
