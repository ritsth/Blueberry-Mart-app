namespace BlueberryMart.Api.Models.Entities;

public class Order
{
    public Guid Id { get; set; }

    /// <summary>Human-friendly sequential order number (e.g. 1042), DB-generated.</summary>
    public int OrderNumber { get; set; }

    /// <summary>Owning customer. Null for an in-store walk-in sale (no customer attached).</summary>
    public Guid? UserId { get; set; }

    public Guid BranchId { get; set; }
    public string OrderType { get; set; } = null!;

    /// <summary>Sales origin: "online" (customer-placed) or "in_store" (rung up by staff at the till).</summary>
    public string Channel { get; set; } = "online";

    public string Status { get; set; } = "pending";
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? DeliveryAddress { get; set; }
    public decimal DeliveryFee { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User? User { get; set; }
    public Branch Branch { get; set; } = null!;
}
