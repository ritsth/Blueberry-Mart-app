using System.Security.Claims;
using BlueberryMart.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(BlueberryMartDbContext context) : ControllerBase
{
    // GET /api/notifications
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var notifications = await context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Id, n.Message, n.InventoryId, n.IsRead, n.CreatedAt })
            .ToListAsync();

        var unread = notifications.Count(n => !n.IsRead);
        return Ok(new { unread, notifications });
    }

    // POST /api/notifications/read  — mark all of the caller's notifications read
    [HttpPost("read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return NoContent();
    }
}
