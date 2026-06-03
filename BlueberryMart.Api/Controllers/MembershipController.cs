using System.Security.Claims;
using BlueberryMart.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/membership")]
[Authorize]
public class MembershipController(BlueberryMartDbContext context) : ControllerBase
{
    public const decimal MemberDiscountRate = 0.05m; // 5% off

    // GET /api/membership/status
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        return Ok(new
        {
            IsMember = user.IsMember,
            MemberSince = user.MemberSince,
            DiscountRate = MemberDiscountRate,
        });
    }

    // POST /api/membership/activate
    [HttpPost("activate")]
    public async Task<IActionResult> Activate()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        if (user.IsMember)
            return Ok(new { IsMember = true, MemberSince = user.MemberSince, message = "Already a member." });

        user.IsMember = true;
        user.MemberSince = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return Ok(new
        {
            IsMember = true,
            MemberSince = user.MemberSince,
            message = "Membership activated. You now get 5% off every order.",
        });
    }
}
