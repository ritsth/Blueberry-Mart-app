namespace BlueberryMart.Api.Models.Requests;

/// <summary>Record an in-store / manual payment for an order (cash, card, …).</summary>
public class RecordPaymentRequest
{
    /// <summary>Payment method label, e.g. "cash" or "card". Defaults to "cash".</summary>
    public string? Method { get; set; }
}
