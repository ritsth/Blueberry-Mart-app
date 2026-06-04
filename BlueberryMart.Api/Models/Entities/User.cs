using System.ComponentModel.DataAnnotations.Schema;

namespace BlueberryMart.Api.Models.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Role { get; set; } = "customer";
    public int LoyaltyPoints { get; set; }

    // Membership: a paid period that runs until MemberUntil. Cancelling stops
    // renewal but keeps benefits until MemberUntil passes.
    public DateTime? MemberSince { get; set; }
    public DateTime? MemberUntil { get; set; }
    public bool MembershipCancelled { get; set; }

    /// <summary>True while the current membership period is still active.</summary>
    [NotMapped]
    public bool IsMember => MemberUntil.HasValue && MemberUntil.Value > DateTime.UtcNow;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Address> Addresses { get; set; } = [];
}
