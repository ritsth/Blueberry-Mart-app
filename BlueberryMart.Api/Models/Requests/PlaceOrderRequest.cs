namespace BlueberryMart.Api.Models.Requests;

public class PlaceOrderRequest
{
    public Guid BranchId { get; set; }
    public string OrderType { get; set; } = null!;
    public Guid? AddressId { get; set; }
    public List<OrderItemRequest> Items { get; set; } = [];
}

public class OrderItemRequest
{
    public Guid ItemId { get; set; }
    public int Quantity { get; set; }
}
