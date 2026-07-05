using BlueberryMart.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueberryMart.Api.Tests;

/// <summary>
/// The cache wrapper must never let a Redis/backend fault surface into the request path: reads
/// degrade to a miss and writes/removes become no-ops. Pure unit tests — no factory/DB.
/// </summary>
public class DistributedCacheResilienceTests
{
    /// <summary>An IDistributedCache whose every operation throws, standing in for an unreachable Redis.</summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        private static Exception Boom() => new InvalidOperationException("redis down");
        public byte[]? Get(string key) => throw Boom();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Boom();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Boom();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Boom();
        public void Refresh(string key) => throw Boom();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Boom();
        public void Remove(string key) => throw Boom();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Boom();
    }

    private static DistributedCacheService NewService() =>
        new(new ThrowingCache(), NullLogger<DistributedCacheService>.Instance);

    [Fact]
    public async Task GetAsync_WhenBackendThrows_ReturnsDefaultInsteadOfThrowing()
    {
        var svc = NewService();
        Assert.Null(await svc.GetAsync<string>("any-key"));
    }

    [Fact]
    public async Task WritesAndInvalidations_WhenBackendThrows_DoNotThrow()
    {
        var svc = NewService();

        // None of these should surface the backend fault.
        await svc.SetAsync("k", "v", TimeSpan.FromSeconds(30));
        await svc.RemoveAsync("k");
        await svc.InvalidateBranchInventoryAsync(Guid.NewGuid());
        await svc.InvalidateUserStatusAsync(Guid.NewGuid());
    }
}
