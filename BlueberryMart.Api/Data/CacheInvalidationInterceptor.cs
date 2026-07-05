using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlueberryMart.Api.Data;

/// <summary>
/// Evicts cached data whenever the underlying rows change, from a single place instead of scattered
/// <c>cache.Remove</c> calls at every write site. Runs on whichever process performed the write
/// (API instance or the background worker); because the cache is shared, that eviction clears the
/// key for all readers.
///
/// Affected keys are captured in <see cref="SavingChangesAsync"/> (while the ChangeTracker still
/// reports Added/Modified) and flushed in <see cref="SavedChangesAsync"/> once the commit succeeds.
/// Registered <b>scoped</b> (one instance per DbContext), so the per-save fields are never touched
/// by two saves at once.
/// </summary>
public class CacheInvalidationInterceptor(ICacheService cache) : SaveChangesInterceptor
{
    private readonly HashSet<Guid> _branchIds = [];
    private readonly HashSet<Guid> _userIds = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = eventData.Context;
        if (context is not null) Capture(context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        foreach (var branchId in _branchIds)
            await cache.InvalidateBranchInventoryAsync(branchId, ct);
        foreach (var userId in _userIds)
            await cache.InvalidateUserStatusAsync(userId, ct);

        _branchIds.Clear();
        _userIds.Clear();
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // The commit didn't happen — drop the pending evictions so a later save on the same
        // context doesn't wrongly clear keys.
        _branchIds.Clear();
        _userIds.Clear();
        base.SaveChangesFailed(eventData);
    }

    private void Capture(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                // Any inventory add/edit changes what a branch's catalogue should return.
                case Inventory inv when entry.State is EntityState.Added or EntityState.Modified:
                    _branchIds.Add(inv.BranchId);
                    break;

                // Only ban / delete / password-reset affect the cached auth snapshot — ignore
                // unrelated user edits (loyalty points, membership, profile) so they don't churn it.
                case User user when entry.State is EntityState.Modified:
                    if (entry.Property(nameof(User.IsBanned)).IsModified
                        || entry.Property(nameof(User.DeletedAt)).IsModified
                        || entry.Property(nameof(User.PasswordChangedAt)).IsModified)
                    {
                        _userIds.Add(user.Id);
                    }
                    break;
            }
        }
    }
}
