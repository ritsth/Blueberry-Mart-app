using System.Security.Claims;
using System.Text.Json;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// Self-service ("Explore") analytics for shareholders: an introspected field catalog,
/// an ad-hoc query endpoint over the BigQuery <c>sales_fact</c> warehouse, and saved
/// report configurations. The query side reports <c>enabled:false</c> when BigQuery
/// isn't configured (e.g. production today); saved reports are plain Postgres rows.
/// </summary>
[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "Shareholder")]
public class AnalyticsController(IAnalyticsQueryService analytics, BlueberryMartDbContext context) : ControllerBase
{
    private Guid ShareholderId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /api/analytics/catalog — the pickable dimensions + measures.
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
        => Ok(await analytics.GetCatalogAsync(ct));

    // POST /api/analytics/query — run a validated, parameterized aggregation.
    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] AnalyticsQueryRequest request, CancellationToken ct)
    {
        if (!analytics.Enabled)
            return Ok(new { enabled = false, columns = Array.Empty<object>(), rows = Array.Empty<object>() });

        try
        {
            return Ok(await analytics.RunQueryAsync(request, ct));
        }
        catch (AnalyticsValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // --- Saved reports (config only, never data) --------------------------------

    // GET /api/analytics/reports — the caller's saved charts (most recent first).
    [HttpGet("reports")]
    public async Task<IActionResult> ListReports(CancellationToken ct)
    {
        var reports = await context.SavedReports
            .Where(r => r.ShareholderId == ShareholderId)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return Ok(reports.Select(Shape));
    }

    // GET /api/analytics/reports/{id}
    [HttpGet("reports/{id:guid}")]
    public async Task<IActionResult> GetReport(Guid id, CancellationToken ct)
    {
        var report = await Find(id, ct);
        return report is null ? NotFound() : Ok(Shape(report));
    }

    // POST /api/analytics/reports — save a new chart config.
    [HttpPost("reports")]
    public async Task<IActionResult> CreateReport([FromBody] SaveReportRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        var report = new SavedReport
        {
            ShareholderId = ShareholderId,
            Name = request.Name.Trim(),
            ConfigJson = request.Config.GetRawText(),
        };
        context.SavedReports.Add(report);
        await context.SaveChangesAsync(ct);

        // Reload to pick up DB-generated defaults (id, timestamps).
        await context.Entry(report).ReloadAsync(ct);
        return CreatedAtAction(nameof(GetReport), new { id = report.Id }, Shape(report));
    }

    // PUT /api/analytics/reports/{id} — rename / replace the config.
    [HttpPut("reports/{id:guid}")]
    public async Task<IActionResult> UpdateReport(Guid id, [FromBody] SaveReportRequest request, CancellationToken ct)
    {
        var report = await Find(id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        report.Name = request.Name.Trim();
        report.ConfigJson = request.Config.GetRawText();
        report.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return Ok(Shape(report));
    }

    // DELETE /api/analytics/reports/{id}
    [HttpDelete("reports/{id:guid}")]
    public async Task<IActionResult> DeleteReport(Guid id, CancellationToken ct)
    {
        var report = await Find(id, ct);
        if (report is null) return NotFound();

        context.SavedReports.Remove(report);
        await context.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<SavedReport?> Find(Guid id, CancellationToken ct) =>
        context.SavedReports.FirstOrDefaultAsync(r => r.Id == id && r.ShareholderId == ShareholderId, ct);

    private static object Shape(SavedReport r) => new
    {
        r.Id,
        r.Name,
        Config = JsonSerializer.Deserialize<JsonElement>(r.ConfigJson),
        r.CreatedAt,
        r.UpdatedAt,
    };
}
