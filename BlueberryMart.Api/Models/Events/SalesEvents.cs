namespace BlueberryMart.Api.Models.Events;

/// <summary>
/// Envelope for every sales event on the <c>sales.events</c> topic. <see cref="Type"/> is one of
/// <see cref="SalesEventTypes"/>; <see cref="Data"/> is the JSON of the matching payload below.
/// One topic keyed by order id keeps an order's events ordered in a single partition.
/// </summary>
public sealed record SalesEventEnvelope(string Type, string Data);

public static class SalesEventTypes
{
    public const string OrderPlaced = "order_placed";
    public const string PaymentStatusChanged = "payment_status_changed";
    public const string ReviewChanged = "review_changed";
    public const string OrderStatusChanged = "order_status_changed";
}

/// <summary>One immutable order line within an <see cref="OrderPlacedEvent"/>.</summary>
public sealed record OrderLineDto(
    Guid OrderLineId,
    Guid ItemId,
    string ItemName,
    bool IsBulk,
    int Quantity,
    decimal UnitPrice,
    /// <summary>1-based line position; line 1 carries the order-level discount/delivery fee.</summary>
    int Rn);

/// <summary>Emitted once per order at placement — the immutable facts behind every sales_fact row.</summary>
public sealed record OrderPlacedEvent(
    Guid OrderId,
    long OrderNumber,
    DateTime OccurredAt,
    string BranchName,
    string OrderType,
    bool IsMember,
    Guid CustomerId,
    decimal OrderDiscount,
    decimal OrderDeliveryFee,
    IReadOnlyList<OrderLineDto> Lines);

/// <summary>Emitted whenever a payment's status changes (initiated / completed / failed).</summary>
public sealed record PaymentStatusChangedEvent(Guid OrderId, string Status, DateTime OccurredAt);

/// <summary>Emitted when a review is submitted (Rating set) or deleted (Rating null = tombstone).</summary>
public sealed record ReviewChangedEvent(Guid OrderId, Guid ItemId, int? Rating, DateTime OccurredAt);

/// <summary>
/// Emitted whenever an order's status changes after placement (confirmed / completed / cancelled).
/// Latest per order wins; the <c>sales_fact</c> view exposes it as the <c>order_status</c> dimension
/// (defaulting to <c>pending</c> for never-changed orders). A cancel of a paid order is a refund:
/// <c>order_status='cancelled'</c> ∩ <c>payment_status='completed'</c>.
/// </summary>
public sealed record OrderStatusChangedEvent(Guid OrderId, string Status, DateTime OccurredAt);
