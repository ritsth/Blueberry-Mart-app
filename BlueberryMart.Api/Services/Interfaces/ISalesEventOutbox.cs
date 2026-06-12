using BlueberryMart.Api.Models.Events;

namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Stages a sales domain event into the transactional outbox. The row is added to the
/// caller's (scoped) <c>BlueberryMartDbContext</c> but <b>not</b> saved — it is persisted
/// atomically by the caller's own <c>SaveChanges</c>/transaction, guaranteeing the event
/// commits exactly with the change that produced it.
/// </summary>
public interface ISalesEventOutbox
{
    void OrderPlaced(OrderPlacedEvent evt);
    void PaymentStatusChanged(PaymentStatusChangedEvent evt);
    void ReviewChanged(ReviewChangedEvent evt);
    void OrderStatusChanged(OrderStatusChangedEvent evt);
}
