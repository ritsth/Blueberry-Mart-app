namespace BlueberryMart.Api.Models.Responses;

/// <summary>A user row as shown in the admin portal's user table.</summary>
public class AdminUserResponse
{
    public Guid Id { get; set; }
    public string? Email { get; set; }      // null for a guest customer (created at the till by phone)
    public string? Phone { get; set; }
    public string Role { get; set; } = null!;
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public bool IsMember { get; set; }
    public int LoyaltyPoints { get; set; }
    public bool IsBanned { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BanReason { get; set; }
    public DateTime? DeletedAt { get; set; }   // set when the user deleted their own account (anonymized)
    public DateTime CreatedAt { get; set; }
}

/// <summary>A paginated slice of users for the admin table.</summary>
public class AdminUserPage
{
    public List<AdminUserResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>A review row for the admin moderation table.</summary>
public class AdminReviewResponse
{
    public Guid Id { get; set; }
    public string UserEmail { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Rating { get; set; }
    public string Comment { get; set; } = null!;
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A paginated slice of reviews for the admin table.</summary>
public class AdminReviewPage
{
    public List<AdminReviewResponse> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
