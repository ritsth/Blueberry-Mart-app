using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.DTOs;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Reads/writes the single StoreSettings row, caching a detached snapshot so the per-checkout read
/// doesn't hit the DB every time. Uses the shared distributed cache (write-through on update), so a
/// settings change is seen across all instances immediately — not stuck in one process's memory.
/// </summary>
public class SettingsService(BlueberryMartDbContext context, ICacheService cache) : ISettingsService
{
    private const string CacheKey = "store_settings";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<StoreSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var cached = await cache.GetAsync<StoreSettingsDto>(CacheKey, ct);
        if (cached is not null) return cached;

        var row = await context.StoreSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                  ?? new StoreSettings(); // defaults if the row somehow isn't seeded yet
        var dto = ToDto(row);
        await cache.SetAsync(CacheKey, dto, Ttl, ct);
        return dto;
    }

    public async Task<StoreSettingsDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var row = await context.StoreSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new StoreSettings();
            context.StoreSettings.Add(row);
        }

        if (request.DeliveryFee is { } fee) row.DeliveryFee = fee;
        if (request.MembershipMonthlyFee is { } mf) row.MembershipMonthlyFee = mf;
        if (request.MemberDiscountRate is { } dr) row.MemberDiscountRate = dr;
        if (request.MaintenanceMode is { } mm) row.MaintenanceMode = mm;
        if (request.MaintenanceMessage is not null)
            row.MaintenanceMessage = string.IsNullOrWhiteSpace(request.MaintenanceMessage)
                ? null : request.MaintenanceMessage.Trim();
        row.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);

        var dto = ToDto(row);
        await cache.SetAsync(CacheKey, dto, Ttl, ct);
        return dto;
    }

    private static StoreSettingsDto ToDto(StoreSettings s) => new(
        s.DeliveryFee, s.MembershipMonthlyFee, s.MemberDiscountRate,
        s.MaintenanceMode, s.MaintenanceMessage, s.UpdatedAt);
}
