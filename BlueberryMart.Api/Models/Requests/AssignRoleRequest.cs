namespace BlueberryMart.Api.Models.Requests;

public class AssignRoleRequest
{
    /// <summary>Target role: customer, shareholder, staff, manager, or admin.</summary>
    public string Role { get; set; } = null!;

    /// <summary>
    /// Branch to assign for staff/manager (required for those roles, ignored for others).
    /// </summary>
    public Guid? BranchId { get; set; }
}
