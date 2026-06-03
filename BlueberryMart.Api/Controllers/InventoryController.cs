using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController(BlueberryMartDbContext context) : ControllerBase
{
    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("customer")]
    public async Task<ActionResult<IEnumerable<Inventory>>> GetForCustomer([FromQuery] Guid branchId)
    {
        var items = await context.Inventory
            .Where(i => i.BranchId == branchId && i.StockQuantity > 0 && !i.IsBulkOnly)
            .ToListAsync();

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
}
