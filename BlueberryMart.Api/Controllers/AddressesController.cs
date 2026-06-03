using System.Security.Claims;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

[ApiController]
[Route("api/addresses")]
[Authorize]
public class AddressesController(BlueberryMartDbContext context) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/addresses
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var addresses = await context.Addresses
            .Where(a => a.UserId == CurrentUserId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.Label,
                a.AddressLine,
                a.City,
                a.Phone,
                a.IsDefault,
            })
            .ToListAsync();

        return Ok(addresses);
    }

    // POST /api/addresses
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Label) ||
            string.IsNullOrWhiteSpace(request.AddressLine) ||
            string.IsNullOrWhiteSpace(request.City))
            return BadRequest(new { message = "Label, address line, and city are required." });

        var userId = CurrentUserId;

        // First address is always the default
        var hasAny = await context.Addresses.AnyAsync(a => a.UserId == userId);
        var makeDefault = request.IsDefault || !hasAny;

        if (makeDefault)
            await ClearExistingDefaultAsync(userId);

        var address = new Address
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Label = request.Label.Trim(),
            AddressLine = request.AddressLine.Trim(),
            City = request.City.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            IsDefault = makeDefault,
            CreatedAt = DateTime.UtcNow,
        };
        context.Addresses.Add(address);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new
        {
            address.Id,
            address.Label,
            address.AddressLine,
            address.City,
            address.Phone,
            address.IsDefault,
        });
    }

    // PUT /api/addresses/{id}/default
    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        var userId = CurrentUserId;
        var address = await context.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address is null) return NotFound();

        await ClearExistingDefaultAsync(userId);
        address.IsDefault = true;
        await context.SaveChangesAsync();

        return Ok(new { address.Id, address.IsDefault });
    }

    // DELETE /api/addresses/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = CurrentUserId;
        var address = await context.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address is null) return NotFound();

        var wasDefault = address.IsDefault;
        context.Addresses.Remove(address);
        await context.SaveChangesAsync();

        // Promote another address to default if we removed the default one
        if (wasDefault)
        {
            var next = await context.Addresses
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();
            if (next is not null)
            {
                next.IsDefault = true;
                await context.SaveChangesAsync();
            }
        }

        return NoContent();
    }

    private async Task ClearExistingDefaultAsync(Guid userId)
    {
        var current = await context.Addresses
            .Where(a => a.UserId == userId && a.IsDefault)
            .ToListAsync();
        foreach (var a in current)
            a.IsDefault = false;
    }
}
