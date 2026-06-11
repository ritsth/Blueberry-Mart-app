namespace BlueberryMart.Api.Services.Interfaces;

public interface IOrderExpiryService
{
    /// <summary>
    /// Cancels pending orders older than <paramref name="holdWindow"/> that have no completed
    /// payment, returns their reserved stock, and emits a stock-change event per item (reason
    /// "order_expired"). Returns the number of orders expired.
    /// </summary>
    Task<int> SweepExpiredAsync(TimeSpan holdWindow, CancellationToken ct = default);
}
