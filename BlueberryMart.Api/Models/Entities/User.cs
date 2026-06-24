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

    // New password sign-ups must confirm their email (via a one-time link) before they can log in.
    // Existing rows are grandfathered to true in the migration; Google sign-ins are set true since
    // Google already vouches for the address. Guests (phone-only) stay false until they claim+verify.
    public bool EmailVerified { get; set; }

    // Google account id (the OAuth `sub`) when the user signed in with Google. Null for
    // password-only accounts. A Google sign-in with no matching account creates a customer
    // with PasswordHash null. Cleared on account deletion so it can't be resurrected.
    public string? GoogleId { get; set; }

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

    // Set when the user deletes their own account (Google Play requirement). The row is kept and
    // anonymized — Email/PasswordHash/Phone are scrubbed — so orders/reviews stay intact for
    // analytics, but the account can no longer sign in (enforced like a ban, per-request).
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<Address> Addresses { get; set; } = [];
}
