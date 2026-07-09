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
    // GET /api/notifications?limit=&offset=
    // Bounded so a long-lived account can't return an unbounded list. Params are optional and the
    // response shape ({ unread, notifications }) is unchanged, so existing clients keep working.
    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        var mine = context.Notifications.Where(n => n.UserId == userId);

        // Count unread over ALL of the user's notifications, not just the returned page, so the
        // badge stays correct once there are more than `limit` of them.
        var unread = await mine.CountAsync(n => !n.IsRead);

        var notifications = await mine
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(n => new { n.Id, n.Message, n.InventoryId, n.IsRead, n.CreatedAt })
            .ToListAsync();

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
