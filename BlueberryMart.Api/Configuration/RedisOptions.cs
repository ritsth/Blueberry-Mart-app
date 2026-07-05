namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Distributed-cache settings, bound from the <c>"Redis"</c> section. An empty
/// <see cref="ConnectionString"/> ⇒ the app falls back to an in-process
/// <c>AddDistributedMemoryCache</c> (local dev, CI, tests — no Redis required); a real
/// Memorystore/Redis endpoint turns on the shared cache in production. Either way the code
/// talks to <c>IDistributedCache</c>, so nothing above the wrapper changes.
/// </summary>
public class RedisOptions
{
    /// <summary>StackExchange.Redis connection string, e.g. <c>10.0.0.3:6379</c>. Empty ⇒ in-memory fallback.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Key prefix so all Blueberry Mart keys share a namespace in a shared Redis.</summary>
    public string InstanceName { get; set; } = "bbm:";

    /// <summary>TTL for cached branch inventory catalogues.</summary>
    public int InventoryTtlSeconds { get; set; } = 60;

    /// <summary>
    /// TTL for the cached per-request auth status (ban/delete/password-reset). Kept short; it is
    /// only a backstop because the cache is evicted proactively whenever those fields change.
    /// </summary>
    public int AuthStatusTtlSeconds { get; set; } = 60;
}
