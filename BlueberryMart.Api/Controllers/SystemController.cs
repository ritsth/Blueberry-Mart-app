using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueberryMart.Api.Controllers;

/// <summary>Public, unauthenticated status the mobile app can poll (e.g. to show a maintenance banner).</summary>
[ApiController]
[Route("api/system")]
public class SystemController(ISettingsService settings) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var s = await settings.GetAsync();
        return Ok(new
        {
            maintenanceMode = s.MaintenanceMode,
            maintenanceMessage = s.MaintenanceMessage,
            deliveryFee = s.DeliveryFee,
            membershipMonthlyFee = s.MembershipMonthlyFee,
            memberDiscountRate = s.MemberDiscountRate,
        });
    }
}
