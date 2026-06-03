using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/reviews")]
[Authorize(Roles = "Customer,Shareholder")]
public class ReviewsController(BlueberryMartDbContext context, IWebHostEnvironment env) : ControllerBase
{
    private const int TextReviewPoints  = 10;
    private const int PhotoReviewPoints = 20;

    // POST /api/reviews
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitReview(
        [FromForm] Guid orderId,
        [FromForm] Guid itemId,
        [FromForm] int rating,
        [FromForm] string comment,
        IFormFile? image)
    {
        if (rating < 1 || rating > 5)
            return BadRequest(new { message = "Rating must be between 1 and 5." });

        if (string.IsNullOrWhiteSpace(comment))
            return BadRequest(new { message = "Comment is required." });

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Verify the user placed the specified order and it contains the item
        var orderBelongsToUser = await context.Orders
            .AnyAsync(o => o.Id == orderId && o.UserId == userId);

        if (!orderBelongsToUser)
            return Forbid();

        var order = await context.Orders.FindAsync(orderId);
        var itemExistsInBranch = await context.Inventory
            .AnyAsync(i => i.Id == itemId && i.BranchId == order!.BranchId);

        if (!itemExistsInBranch)
            return BadRequest(new { message = "The specified item was not part of this order's branch." });

        // Prevent duplicate reviews for the same order + item
        var alreadyReviewed = await context.Reviews
            .AnyAsync(r => r.UserId == userId && r.OrderId == orderId && r.ItemId == itemId);

        if (alreadyReviewed)
            return Conflict(new { message = "You have already reviewed this item for this order." });

        string? savedImagePath = null;
        if (image is not null)
        {
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(image.ContentType.ToLower()))
                return BadRequest(new { message = "Only JPEG, PNG, and WebP images are allowed." });

            var uploadDir = Path.Combine(env.WebRootPath, "images", "reviews");
            Directory.CreateDirectory(uploadDir);

            var ext      = Path.GetExtension(image.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(uploadDir, fileName);

            await using var stream = System.IO.File.Create(fullPath);
            await image.CopyToAsync(stream);

            // Store a relative path for future cloud bucket migration
            savedImagePath = $"/images/reviews/{fileName}";
        }

        var review = new Review
        {
            Id        = Guid.NewGuid(),
            UserId    = userId,
            OrderId   = orderId,
            ItemId    = itemId,
            Rating    = rating,
            Comment   = comment,
            ImagePath = savedImagePath,
            CreatedAt = DateTime.UtcNow
        };
        context.Reviews.Add(review);

        // Credit loyalty points
        var user = await context.Users.FindAsync(userId);
        if (user is not null)
        {
            user.LoyaltyPoints += savedImagePath is not null ? PhotoReviewPoints : TextReviewPoints;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(SubmitReview), new { id = review.Id }, new
        {
            review.Id,
            review.Rating,
            review.ImagePath,
            LoyaltyPointsEarned = savedImagePath is not null ? PhotoReviewPoints : TextReviewPoints
        });
    }
}
