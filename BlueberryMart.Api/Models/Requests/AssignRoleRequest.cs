namespace BlueberryMart.Api.Models.Requests;

public class AssignRoleRequest
{
    /// <summary>Target role: customer, shareholder, staff, manager, or admin.</summary>
    public string Role { get; set; } = null!;
}
