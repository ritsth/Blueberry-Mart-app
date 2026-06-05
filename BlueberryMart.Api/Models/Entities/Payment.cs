namespace BlueberryMart.Api.Models.Entities;

public class Payment
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    /// <summary>Our reference sent to eSewa as <c>transaction_uuid</c>.</summary>
    public string TransactionUuid { get; set; } = null!;

    public decimal Amount { get; set; }

    /// <summary>One of <c>initiated</c>, <c>completed</c>, <c>failed</c> (payment_status enum).</summary>
    public string Status { get; set; } = "initiated";

    /// <summary>eSewa <c>transaction_code</c> returned once the payment completes.</summary>
    public string? ProviderRef { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Order Order { get; set; } = null!;
}
