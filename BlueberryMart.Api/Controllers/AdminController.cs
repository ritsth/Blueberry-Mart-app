using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.DTOs;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Models.Responses;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Admin operations consumed by the separate web portal. Every endpoint requires the
/// Admin role — this is the real security boundary (keeping admin code out of the
/// mobile app is only defence-in-depth). Ban takes effect immediately because the
/// JwtBearer OnTokenValidated hook rejects banned users on every request.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(BlueberryMartDbContext context, ISettingsService settings) : ControllerBase
{
    private static readonly string[] AssignableRoles = ["customer", "shareholder", "staff", "manager", "admin"];

    [HttpGet("users")]
    public async Task<ActionResult<AdminUserPage>> ListUsers(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? banned,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = context.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLower()}%";
            query = query.Where(u => EF.Functions.ILike(u.Email, term));
        }
        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role == role.ToLower());
        if (banned.HasValue)
            query = query.Where(u => u.IsBanned == banned.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserResponse
            {
                Id = u.Id,
                Email = u.Email,
                Role = u.Role,
                BranchId = u.BranchId,
                BranchName = u.BranchId != null ? u.Branch!.Name : null,
                IsMember = u.Role == "shareholder" || u.Role == "admin"
                    || (u.MemberUntil.HasValue && u.MemberUntil.Value > DateTime.UtcNow),
                LoyaltyPoints = u.LoyaltyPoints,
                IsBanned = u.IsBanned,
                BannedAt = u.BannedAt,
                BanReason = u.BanReason,
                CreatedAt = u.CreatedAt,
            })
            .ToListAsync();

        return Ok(new AdminUserPage { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    [HttpPost("users/{id:guid}/ban")]
    public async Task<IActionResult> Ban(Guid id, [FromBody] BanUserRequest request)
    {
        var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (id == callerId)
            return BadRequest(new { message = "You cannot ban your own account." });

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User not found." });
        if (user.Role == "admin")
            return BadRequest(new { message = "Admin accounts cannot be banned." });

        if (!user.IsBanned)
        {
            user.IsBanned = true;
            user.BannedAt = DateTime.UtcNow;
            user.BanReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return Ok(new { user.Id, user.IsBanned, user.BannedAt, user.BanReason });
    }

    [HttpPost("users/{id:guid}/unban")]
    public async Task<IActionResult> Unban(Guid id)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User not found." });

        if (user.IsBanned)
        {
            user.IsBanned = false;
            user.BannedAt = null;
            user.BanReason = null;
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return Ok(new { user.Id, user.IsBanned });
    }

    [HttpGet("reviews")]
    public async Task<ActionResult<AdminReviewPage>> ListReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = context.Reviews.AsNoTracking();
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminReviewResponse
            {
                Id = r.Id,
                UserEmail = r.User.Email,
                ItemName = r.Item.ItemName,
                Rating = r.Rating,
                Comment = r.Comment,
                ImagePath = r.ImagePath,
                CreatedAt = r.CreatedAt,
            })
            .ToListAsync();

        return Ok(new AdminReviewPage { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    [HttpPost("users/{id:guid}/role")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request)
    {
        var role = request.Role?.Trim().ToLower() ?? "";
        if (!AssignableRoles.Contains(role))
            return BadRequest(new { message = $"Role must be one of: {string.Join(", ", AssignableRoles)}." });

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "User not found." });

        // Don't let the last admin be demoted (avoids locking everyone out).
        if (user.Role == "admin" && role != "admin")
        {
            var otherAdmins = await context.Users.CountAsync(u => u.Role == "admin" && u.Id != id);
            if (otherAdmins == 0)
                return BadRequest(new { message = "Cannot demote the last remaining admin." });
        }

        // Staff and managers operate a single branch; everyone else is branch-agnostic.
        if (role is "staff" or "manager")
        {
            if (request.BranchId is not { } branchId)
                return BadRequest(new { message = "Staff and manager accounts must be assigned a branch." });
            if (!await context.Branches.AnyAsync(b => b.Id == branchId && b.IsActive))
                return BadRequest(new { message = "The selected branch does not exist." });
            user.BranchId = branchId;
        }
        else
        {
            user.BranchId = null;
        }

        user.Role = role;
        if (context.ChangeTracker.HasChanges())
        {
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return Ok(new { user.Id, user.Role, user.BranchId });
    }

    [HttpGet("settings")]
    public async Task<ActionResult<StoreSettingsDto>> GetSettings()
        => Ok(await settings.GetAsync());

    [HttpPut("settings")]
    public async Task<ActionResult<StoreSettingsDto>> UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        if (request.MemberDiscountRate is { } r && (r < 0 || r > 1))
            return BadRequest(new { message = "MemberDiscountRate must be between 0 and 1 (e.g. 0.05 = 5%)." });
        if (request.DeliveryFee is { } d && d < 0)
            return BadRequest(new { message = "DeliveryFee cannot be negative." });
        if (request.MembershipMonthlyFee is { } m && m < 0)
            return BadRequest(new { message = "MembershipMonthlyFee cannot be negative." });

        return Ok(await settings.UpdateAsync(request));
    }

    [HttpDelete("reviews/{id:guid}")]
    public async Task<IActionResult> DeleteReview(Guid id)
    {
        var review = await context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound(new { message = "Review not found." });

        context.Reviews.Remove(review);
        await context.SaveChangesAsync();
        return NoContent();
    }
}
