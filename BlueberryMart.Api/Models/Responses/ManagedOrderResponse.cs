namespace BlueberryMart.Api.Models.Responses;

/// <summary>An order row in the back-office fulfillment table.</summary>
public class ManagedOrderResponse
{
    public Guid Id { get; set; }
    public int OrderNumber { get; set; }
    public string CustomerEmail { get; set; } = null!;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public string OrderType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    /// <summary>"unpaid" when there's no payment row, else the payment's status.</summary>
    public string PaymentStatus { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class ManagedOrderLineItem
{
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>Full order detail for the fulfillment view.</summary>
public class ManagedOrderDetailResponse : ManagedOrderResponse
{
    public decimal DiscountAmount { get; set; }
    public decimal DeliveryFee { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? PaymentRef { get; set; }
    public List<ManagedOrderLineItem> Items { get; set; } = [];
}

public class ManagedOrderPage
{
    public List<ManagedOrderResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
