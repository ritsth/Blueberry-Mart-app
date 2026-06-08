namespace BlueberryMart.Api.Models.Entities;

/// <summary>
/// Single-row table of admin-editable global settings (previously hardcoded constants).
/// Read through ISettingsService (cached); edited from the admin portal.
/// </summary>
public class StoreSettings
{
    /// <summary>Fixed singleton id — there is only ever one row.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    /// <summary>Flat delivery fee in Rs (waived for members). Was OrdersController.DeliveryFee.</summary>
    public decimal DeliveryFee { get; set; } = 100m;

    /// <summary>Monthly membership price in Rs. Was MembershipController.MonthlyFee.</summary>
    public decimal MembershipMonthlyFee { get; set; } = 199m;

    /// <summary>Member discount as a fraction (0.05 = 5%). Was MembershipController.MemberDiscountRate.</summary>
    public decimal MemberDiscountRate { get; set; } = 0.05m;

    /// <summary>When true, customers cannot place orders (checkout returns 503).</summary>
    public bool MaintenanceMode { get; set; }

    /// <summary>Optional message shown to customers while maintenance mode is on.</summary>
    public string? MaintenanceMessage { get; set; }

    public DateTime UpdatedAt { get; set; }
}
