using System.Security.Claims;
using BlueberryMart.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(BlueberryMartDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var orders = await context.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                BranchName = o.Branch.Name,
                o.OrderType,
                o.Status,
                o.TotalAmount,
                o.CreatedAt,
                Items = context.OrderItems
                    .Where(oi => oi.OrderId == o.Id)
                    .Select(oi => new
                    {
                        oi.Item.ItemName,
                        oi.Quantity,
                        oi.UnitPrice,
                    })
                    .ToList(),
            })
            .ToListAsync();

        var reviews = await context.Reviews
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                ItemName  = r.Item.ItemName,
                r.Rating,
                r.Comment,
                r.ImagePath,
                r.CreatedAt,
            })
            .ToListAsync();

        return Ok(new
        {
            Email         = user.Email,
            Role          = user.Role,
            LoyaltyPoints = user.LoyaltyPoints,
            MemberSince   = user.CreatedAt,
            TotalOrders   = orders.Count,
            TotalSpent    = orders.Sum(o => o.TotalAmount),
            Orders        = orders,
            Reviews       = reviews,
        });
    }
}
