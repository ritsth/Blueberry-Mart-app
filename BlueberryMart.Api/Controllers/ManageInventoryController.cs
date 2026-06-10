using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Events;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Models.Responses;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Back-office catalogue management for store staff. Branch-scoped: staff and
/// managers may only touch items in their own branch (the JWT `branch` claim);
/// admins may touch any branch. Deactivate/restore is manager/admin only.
/// </summary>
[ApiController]
[Route("api/inventory/manage")]
[Authorize(Roles = "Staff,Manager,Admin")]
public class ManageInventoryController(BlueberryMartDbContext context, IStockEventProducer stockEvents) : ControllerBase
{
    private const int LowStockThreshold = 5;

    private bool IsAdmin => User.IsInRole("Admin");

    private Guid CallerId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid? CallerBranch() =>
        Guid.TryParse(User.FindFirstValue("branch"), out var b) ? b : null;

    // Returns an error result if the caller may not operate the given branch, else null.
    private ActionResult? GuardBranch(Guid branchId)
    {
        if (IsAdmin) return null;
        var mine = CallerBranch();
        if (mine is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });
        if (mine.Value != branchId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only manage items in your own branch." });
        return null;
    }

    [HttpGet]
    public async Task<ActionResult<InventoryItemPage>> List(
        [FromQuery] Guid? branchId,
        [FromQuery] string? search,
        [FromQuery] bool lowStock = false,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = context.Inventory.AsNoTracking().AsQueryable();

        // Non-admins are locked to their own branch, ignoring any branchId param.
        if (!IsAdmin)
        {
            var mine = CallerBranch();
            if (mine is null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Your account is not assigned to a branch." });
            query = query.Where(i => i.BranchId == mine.Value);
        }
        else if (branchId.HasValue)
        {
            query = query.Where(i => i.BranchId == branchId.Value);
        }

        if (!includeInactive)
            query = query.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => EF.Functions.ILike(i.ItemName, $"%{search.Trim()}%"));
        if (lowStock)
            query = query.Where(i => i.StockQuantity <= LowStockThreshold);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.ItemName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new InventoryItemResponse
            {
                Id = i.Id,
                BranchId = i.BranchId,
                BranchName = i.Branch.Name,
                ItemName = i.ItemName,
                Price = i.Price,
                StockQuantity = i.StockQuantity,
                IsBulkOnly = i.IsBulkOnly,
                IsActive = i.IsActive,
                UpdatedAt = i.UpdatedAt,
            })
            .ToListAsync();

        return Ok(new InventoryItemPage { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    [HttpPost]
    public async Task<ActionResult<InventoryItemResponse>> Create([FromBody] CreateInventoryItemRequest request)
    {
        var name = request.ItemName?.Trim() ?? "";
        if (name.Length < 2)
            return BadRequest(new { message = "Item name must be at least 2 characters." });
        if (request.Price < 0)
            return BadRequest(new { message = "Price cannot be negative." });
        if (request.StockQuantity < 0)
            return BadRequest(new { message = "Stock quantity cannot be negative." });

        if (GuardBranch(request.BranchId) is { } denied) return denied;

        var branch = await context.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId && b.IsActive);
        if (branch is null)
            return BadRequest(new { message = "The selected branch does not exist." });

        var item = new Inventory
        {
            Id = Guid.NewGuid(),
            BranchId = request.BranchId,
            ItemName = name,
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            IsBulkOnly = request.IsBulkOnly,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Inventory.Add(item);
        await context.SaveChangesAsync();

        return Created($"/api/inventory/manage/{item.Id}", Map(item, branch.Name));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryItemResponse>> Update(Guid id, [FromBody] UpdateInventoryItemRequest request)
    {
        var name = request.ItemName?.Trim() ?? "";
        if (name.Length < 2)
            return BadRequest(new { message = "Item name must be at least 2 characters." });
        if (request.Price < 0)
            return BadRequest(new { message = "Price cannot be negative." });

        var item = await context.Inventory.Include(i => i.Branch).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound(new { message = "Item not found." });
        if (GuardBranch(item.BranchId) is { } denied) return denied;

        item.ItemName = name;
        item.Price = request.Price;
        item.IsBulkOnly = request.IsBulkOnly;
        item.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return Ok(Map(item, item.Branch.Name));
    }

    [HttpPost("{id:guid}/adjust")]
    public async Task<ActionResult<InventoryItemResponse>> Adjust(Guid id, [FromBody] AdjustStockRequest request)
    {
        if (request.Delta == 0)
            return BadRequest(new { message = "Delta must be non-zero." });

        var item = await context.Inventory.Include(i => i.Branch).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound(new { message = "Item not found." });
        if (GuardBranch(item.BranchId) is { } denied) return denied;

        var newQty = item.StockQuantity + request.Delta;
        if (newQty < 0)
            return BadRequest(new { message = $"Adjustment would make stock negative (current {item.StockQuantity})." });

        var oldQty = item.StockQuantity;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "adjustment" : request.Reason!.Trim();
        var now = DateTime.UtcNow;
        item.StockQuantity = newQty;
        item.UpdatedAt = now;

        // Audit row: who adjusted, by how much, why, and the resulting quantity.
        context.StockAdjustments.Add(new StockAdjustment
        {
            Id = Guid.NewGuid(),
            InventoryId = item.Id,
            BranchId = item.BranchId,
            UserId = CallerId(),
            Delta = request.Delta,
            NewQuantity = newQty,
            Reason = reason,
            CreatedAt = now,
        });
        await context.SaveChangesAsync();

        // Feeds the Kafka stock pipeline / back-in-stock notifications, like restock.
        stockEvents.Publish(new StockChangedEvent(
            ItemId: item.Id,
            BranchId: item.BranchId,
            ItemName: item.ItemName,
            OldQuantity: oldQty,
            NewQuantity: newQty,
            Reason: reason,
            OccurredAt: now));

        return Ok(Map(item, item.Branch.Name));
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<StockAdjustmentResponse>>> History(Guid id)
    {
        var item = await context.Inventory.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound(new { message = "Item not found." });
        if (GuardBranch(item.BranchId) is { } denied) return denied;

        var rows = await context.StockAdjustments.AsNoTracking()
            .Where(s => s.InventoryId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .Select(s => new StockAdjustmentResponse
            {
                CreatedAt = s.CreatedAt,
                UserEmail = s.User.Email,
                Delta = s.Delta,
                NewQuantity = s.NewQuantity,
                Reason = s.Reason,
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = "Manager,Admin")]
    public Task<ActionResult<InventoryItemResponse>> Deactivate(Guid id) => SetActive(id, false);

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = "Manager,Admin")]
    public Task<ActionResult<InventoryItemResponse>> Activate(Guid id) => SetActive(id, true);

    private async Task<ActionResult<InventoryItemResponse>> SetActive(Guid id, bool active)
    {
        var item = await context.Inventory.Include(i => i.Branch).FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return NotFound(new { message = "Item not found." });
        if (GuardBranch(item.BranchId) is { } denied) return denied;

        if (item.IsActive != active)
        {
            item.IsActive = active;
            item.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        return Ok(Map(item, item.Branch.Name));
    }

    private static InventoryItemResponse Map(Inventory i, string branchName) => new()
    {
        Id = i.Id,
        BranchId = i.BranchId,
        BranchName = branchName,
        ItemName = i.ItemName,
        Price = i.Price,
        StockQuantity = i.StockQuantity,
        IsBulkOnly = i.IsBulkOnly,
        IsActive = i.IsActive,
        UpdatedAt = i.UpdatedAt,
    };
}
