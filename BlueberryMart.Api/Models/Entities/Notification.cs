namespace BlueberryMart.Api.Models.Entities;

/// <summary>An in-app notification for a user (e.g. "X is back in stock").</summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Message { get; set; } = null!;

    /// <summary>Optional context — the inventory item the notification is about.</summary>
    public Guid? InventoryId { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
