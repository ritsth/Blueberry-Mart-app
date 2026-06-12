using BlueberryMart.Api.Models.Entities;

namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Cancels an order — restores the stock reserved at placement, marks it <c>cancelled</c>, and
/// emits the stock + sales events. The caller loads the order and enforces authorization and the
/// status guard; this performs the state change atomically. Shared by the manager cancel
/// (<c>ManageOrdersController</c>) and the customer self-cancel (<c>OrdersController</c>).
/// </summary>
public interface IOrderCancellationService
{
    Task CancelAsync(Order order, CancellationToken ct = default);
}
