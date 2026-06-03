namespace BlueberryMart.Api.Models.Entities;

public class Order
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
    public string OrderType { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? DeliveryAddress { get; set; }
    public decimal DeliveryFee { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
