namespace BlueberryMart.Api.Models.Requests;

/// <summary>Admin edit of global settings. All fields optional — only provided ones change.</summary>
public class UpdateSettingsRequest
{
    public decimal? DeliveryFee { get; set; }
    public decimal? MembershipMonthlyFee { get; set; }
    /// <summary>Member discount as a fraction (0.05 = 5%).</summary>
    public decimal? MemberDiscountRate { get; set; }
    public bool? MaintenanceMode { get; set; }
    public string? MaintenanceMessage { get; set; }
}
