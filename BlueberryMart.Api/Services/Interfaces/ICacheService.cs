namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// A resilient wrapper over <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
/// Every operation swallows cache/Redis faults: a read fault is treated as a miss (the caller falls
/// back to the source of truth) and a write/remove fault is a no-op. A Redis outage therefore
/// degrades performance but never fails a request.
/// </summary>
public interface ICacheService
{
    /// <summary>Returns the cached value, or <c>default</c> on a miss or any cache fault.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> for <paramref name="ttl"/>. Faults are swallowed.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Removes a single key. Faults are swallowed.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Evicts every cached catalogue variant for a branch (called after any inventory write).</summary>
    Task InvalidateBranchInventoryAsync(Guid branchId, CancellationToken ct = default);

    /// <summary>Evicts a user's cached auth status (called after ban / delete / password reset).</summary>
    Task InvalidateUserStatusAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Central cache-key formats, shared by readers and the invalidation interceptor.</summary>
public static class CacheKeys
{
    public const string CustomerCatalog = "customer";
    public const string BulkCatalog = "bulk";

    /// <summary>All catalogue variants that <see cref="ICacheService.InvalidateBranchInventoryAsync"/> must clear.</summary>
    public static readonly string[] Catalogs = [CustomerCatalog, BulkCatalog];

    public static string InventoryCatalog(Guid branchId, string catalog, bool includeOutOfStock)
        => $"inv:{branchId}:{catalog}:{(includeOutOfStock ? "all" : "instock")}";

    public static string UserStatus(Guid userId) => $"authstatus:{userId}";
}
