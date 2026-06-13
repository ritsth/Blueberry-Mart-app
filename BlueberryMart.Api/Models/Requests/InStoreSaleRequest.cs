namespace BlueberryMart.Api.Models.Requests;

/// <summary>
/// A walk-in sale rung up by staff at the till. The order is created already paid and
/// <c>completed</c> (channel <c>in_store</c>) — it never enters the fulfilment chain.
/// </summary>
public class InStoreSaleRequest
{
    /// <summary>Branch the sale is rung up at. Optional for staff/managers (taken from their
    /// branch claim); required for admins, who aren't tied to a branch.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Items and quantities being sold. Reuses the same shape as a customer order.</summary>
    public List<OrderItemRequest> Items { get; set; } = [];

    /// <summary>Payment method label, e.g. "cash", "card", "esewa". Defaults to "cash".
    /// Recorded as the payment's provider ref (<c>instore:&lt;method&gt;</c>); no gateway is called.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>Optional existing customer to attribute the sale to (for loyalty / history).
    /// When omitted, the sale is booked against the system "Walk-in" customer.</summary>
    public Guid? CustomerId { get; set; }
}
