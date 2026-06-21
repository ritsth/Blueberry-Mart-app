using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Validation;
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
                o.OrderNumber,
                BranchName = o.Branch.Name,
                o.OrderType,
                o.Status,
                o.TotalAmount,
                o.DiscountAmount,
                o.DeliveryFee,
                o.DeliveryAddress,
                o.CreatedAt,
                Items = context.OrderItems
                    .Where(oi => oi.OrderId == o.Id)
                    .Select(oi => new
                    {
                        oi.ItemId,
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
                ItemName = r.Item.ItemName,
                r.Rating,
                r.Comment,
                r.ImagePath,
                r.CreatedAt,
            })
            .ToListAsync();

        return Ok(new
        {
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role,
            LoyaltyPoints = user.LoyaltyPoints,
            MemberSince = user.CreatedAt,
            IsMember = user.IsMember,
            MembershipSince = user.MemberSince,
            MemberUntil = user.MemberUntil,
            MembershipCancelled = user.MembershipCancelled,
            TotalOrders = orders.Count,
            TotalSpent = orders.Sum(o => o.TotalAmount),
            Orders = orders,
            Reviews = reviews,
        });
    }

    // POST /api/profile/link-phone  { phone }
    // Adds a phone to the signed-in account. If a till "guest" (phone-only, no email) exists with
    // that phone, it's *merged* into this account — its orders are reassigned and its loyalty added,
    // then the guest row is removed — so the customer claims the points they earned in store.
    [HttpPost("link-phone")]
    public async Task<IActionResult> LinkPhone([FromBody] LinkPhoneRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(user.Phone))
            return Conflict(new { message = "Your account already has a phone number." });

        if (!PhoneNumber.TryNormalize(request.Phone, out var phone))
            return BadRequest(new { message = "Enter a valid phone number (up to 10 digits)." });

        var byPhone = await context.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (byPhone is not null && byPhone.Id != userId)
        {
            if (byPhone.Email is not null)
                return Conflict(new { message = "This phone number is linked to another account." });

            // Merge the guest into this account, then free its phone before claiming it.
            await using var tx = await context.Database.BeginTransactionAsync();
            await context.Orders.Where(o => o.UserId == byPhone.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, userId));
            user.LoyaltyPoints += byPhone.LoyaltyPoints;
            context.Users.Remove(byPhone);
            await context.SaveChangesAsync();

            user.Phone = phone;
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            await tx.CommitAsync();
        }
        else
        {
            user.Phone = phone;
            user.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return Ok(new { user.Phone, user.LoyaltyPoints });
    }

    // DELETE /api/profile — the user deletes their own account (required by Google Play).
    // A hard delete is impossible (Orders/Reviews/StockAdjustments use ON DELETE RESTRICT and
    // feed the sales_fact analytics), so we anonymize instead: scrub the user's PII and remove
    // their personal data (addresses, notifications, stock subscriptions, saved reports), while
    // keeping order/review rows linked to the now-anonymized account. DeletedAt blocks any
    // further sign-in or use of an outstanding token (see Program.cs OnTokenValidated).
    [HttpDelete]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await context.Users.FindAsync(userId);
        if (user is null) return NotFound();
        if (user.DeletedAt is not null) return NoContent();

        await using var tx = await context.Database.BeginTransactionAsync();

        await context.Addresses.Where(a => a.UserId == userId).ExecuteDeleteAsync();
        await context.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();
        await context.StockSubscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync();
        await context.SavedReports.Where(r => r.ShareholderId == userId).ExecuteDeleteAsync();

        user.Email = null;
        user.PasswordHash = null;
        user.Phone = null;
        user.GoogleId = null;   // so a later Google sign-in starts a fresh account, not this one
        user.MembershipCancelled = true;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        await tx.CommitAsync();
        return NoContent();
    }
}
