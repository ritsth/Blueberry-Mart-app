namespace BlueberryMart.Api.Models.DTOs;

/// <summary>Detached snapshot of the store settings, safe to cache across request scopes.</summary>
public record StoreSettingsDto(
    decimal DeliveryFee,
    decimal MembershipMonthlyFee,
    decimal MemberDiscountRate,
    bool MaintenanceMode,
    string? MaintenanceMessage,
    DateTime UpdatedAt);
