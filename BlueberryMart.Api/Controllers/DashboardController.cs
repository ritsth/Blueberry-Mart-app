using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Back-office dashboard summary. Branch-scoped: staff/managers see their own
/// branch's counts; admins see totals across all branches.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Staff,Manager,Admin")]
public class DashboardController(BlueberryMartDbContext context) : ControllerBase
{
    private const int LowStockThreshold = 5;

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummary>> Summary()
    {
        var isAdmin = User.IsInRole("Admin");
        Guid? branch = Guid.TryParse(User.FindFirstValue("branch"), out var b) ? b : null;
        if (!isAdmin && branch is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });

        var items = context.Inventory.AsNoTracking().Where(i => i.IsActive);
        var orders = context.Orders.AsNoTracking().AsQueryable();
        if (!isAdmin)
        {
            items = items.Where(i => i.BranchId == branch!.Value);
            orders = orders.Where(o => o.BranchId == branch!.Value);
        }

        return Ok(new DashboardSummary
        {
            LowStockItems = await items.CountAsync(i => i.StockQuantity <= LowStockThreshold),
            PendingOrders = await orders.CountAsync(o => o.Status == "pending"),
            ActiveOrders = await orders.CountAsync(o => o.Status == "confirmed" || o.Status == "processing" || o.Status == "ready"),
        });
    }
}
