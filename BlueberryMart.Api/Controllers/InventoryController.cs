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

    [Authorize(Roles = "Customer,Shareholder")]
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());

        var term = q.Trim();

        var matches = await context.Inventory
            .Where(i => i.StockQuantity > 0 && !i.IsBulkOnly &&
                        EF.Functions.ILike(i.ItemName, $"%{term}%"))
            .Include(i => i.Branch)
            .OrderBy(i => i.Branch.Name)
            .ThenBy(i => i.ItemName)
            .Select(i => new
            {
                Id = i.Id,
                ItemName = i.ItemName,
                Price = i.Price,
                StockQuantity = i.StockQuantity,
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
                Items = g.Select(i => new { i.Id, i.ItemName, i.Price, i.StockQuantity }).ToList(),
            });

        return Ok(grouped);
    }
}
