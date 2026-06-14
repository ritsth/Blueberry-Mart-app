using System.ComponentModel.DataAnnotations.Schema;

namespace BlueberryMart.Api.Models.Entities;

public class User
{
    public Guid Id { get; set; }

    // Email + PasswordHash are null for a "guest" customer created at the till by phone only
    // (no app login until they claim the account). Phone is the guest's unique lookup key.
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "customer";

    // For back-office field roles (staff/manager): the branch they operate.
    // Null for customers, shareholders, and admins, who aren't tied to one branch.
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int LoyaltyPoints { get; set; }

    // Membership: a paid period that runs until MemberUntil. Cancelling stops
    // renewal but keeps benefits until MemberUntil passes.
    public DateTime? MemberSince { get; set; }
    public DateTime? MemberUntil { get; set; }
    public bool MembershipCancelled { get; set; }

    /// <summary>
    /// True while the user has Blueberry Plus benefits — either an active paid period,
    /// or a role that includes membership automatically (shareholders and admins).
    /// </summary>
    [NotMapped]
    public bool IsMember =>
        Role is "shareholder" or "admin"
        || (MemberUntil.HasValue && MemberUntil.Value > DateTime.UtcNow);

    // Moderation: an admin can ban a user. Enforced per-request (a banned user is
    // rejected even with a still-valid token) — see the JwtBearer OnTokenValidated hook.
    public bool IsBanned { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BanReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Address> Addresses { get; set; } = [];
}
