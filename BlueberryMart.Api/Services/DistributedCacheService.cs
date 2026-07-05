using System.Text.Json;
using System.Text.Json.Serialization;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace BlueberryMart.Api.Services;

/// <summary>
/// <see cref="ICacheService"/> over <see cref="IDistributedCache"/> (Redis in prod, in-memory
/// otherwise). Every call is wrapped so a cache fault degrades to a miss/no-op instead of throwing
/// into the request path — a Redis outage can never 500 the API.
/// </summary>
public class DistributedCacheService(
    IDistributedCache cache,
    ILogger<DistributedCacheService> logger) : ICacheService
{
    // Ignore reference cycles defensively; entities are queried without their Branch nav, but this
    // keeps serialization safe if that ever changes.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed for {Key}; treating as miss.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await cache.SetAsync(key, bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for {Key}; skipping.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache remove failed for {Key}; skipping.", key);
        }
    }

    public async Task InvalidateBranchInventoryAsync(Guid branchId, CancellationToken ct = default)
    {
        // Only a handful of variants per branch (catalog × in-stock flag), so enumerate and remove
        // the known keys rather than needing a Redis pattern-delete.
        foreach (var catalog in CacheKeys.Catalogs)
        {
            await RemoveAsync(CacheKeys.InventoryCatalog(branchId, catalog, includeOutOfStock: false), ct);
            await RemoveAsync(CacheKeys.InventoryCatalog(branchId, catalog, includeOutOfStock: true), ct);
        }
    }

    public Task InvalidateUserStatusAsync(Guid userId, CancellationToken ct = default) =>
        RemoveAsync(CacheKeys.UserStatus(userId), ct);
}
