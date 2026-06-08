using BlueberryMart.Api.Models.DTOs;
using BlueberryMart.Api.Models.Requests;

namespace BlueberryMart.Api.Services.Interfaces;

public interface ISettingsService
{
    /// <summary>Current global settings (cached). Reads are cheap and happen on every checkout.</summary>
    Task<StoreSettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>Applies an admin edit (only non-null fields) and refreshes the cache.</summary>
    Task<StoreSettingsDto> UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default);
}
