namespace BlueberryMart.Api.Models.Entities;

public class Review
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ItemId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = null!;
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Order Order { get; set; } = null!;
    public Inventory Item { get; set; } = null!;
}
