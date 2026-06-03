using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BlueberryMart.Api.Data;

namespace BlueberryMart.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly BlueberryMartDbContext _context;

    public BranchesController(BlueberryMartDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var branches = await _context.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new { id = b.Id, name = b.Name, city = b.LocationCity })
            .ToListAsync();

        return Ok(branches);
    }
}
