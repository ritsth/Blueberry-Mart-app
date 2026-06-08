using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/membership")]
[Authorize]
public class MembershipController(BlueberryMartDbContext context, ISettingsService settings) : ControllerBase
{
    // GET /api/membership/status
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var s = await settings.GetAsync();
        return Ok(new
        {
            IsMember = user.IsMember,
            MemberSince = user.MemberSince,
            MemberUntil = user.MemberUntil,
            Cancelled = user.MembershipCancelled,
            DiscountRate = s.MemberDiscountRate,
            MonthlyFee = s.MembershipMonthlyFee,
        });
    }

    // POST /api/membership/activate  (join or resume — starts a fresh one-month period)
    [HttpPost("activate")]
    public async Task<IActionResult> Activate()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var now = DateTime.UtcNow;
        user.MemberSince = now;
        user.MemberUntil = now.AddMonths(1);
        user.MembershipCancelled = false;
        user.UpdatedAt = now;
        await context.SaveChangesAsync();

        var s = await settings.GetAsync();
        var pct = (s.MemberDiscountRate * 100).ToString("0.#");
        return Ok(new
        {
            IsMember = true,
            user.MemberSince,
            user.MemberUntil,
            Cancelled = false,
            message = $"Membership active. You get {pct}% off and free delivery for a month.",
        });
    }

    // POST /api/membership/cancel  (stop renewal; benefits remain until MemberUntil)
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        if (!user.IsMember)
            return BadRequest(new { message = "You don't have an active membership." });

        user.MembershipCancelled = true;
        user.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return Ok(new
        {
            IsMember = user.IsMember, // still true until MemberUntil passes
            user.MemberUntil,
            Cancelled = true,
            message = "Membership cancelled. You keep your benefits until the period ends.",
        });
    }
}
