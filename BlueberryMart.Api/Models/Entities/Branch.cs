namespace BlueberryMart.Api.Models.Entities;

public class Branch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string LocationCity { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Inventory> Inventory { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
}
