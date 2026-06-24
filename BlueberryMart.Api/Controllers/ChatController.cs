using System.Security.Claims;
using BlueberryMart.Api.Models.Requests;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlueberryMart.Api.Controllers;

/// <summary>
/// In-app customer support assistant. Answers only item/availability questions and
/// order/support issues. Reports <c>enabled:false</c> when no API key is configured.
/// </summary>
[ApiController]
[Route("api/chat")]
[Authorize(Roles = "Customer,Shareholder")]
[EnableRateLimiting("chat")]
public class ChatController(IChatService chat) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (!chat.Enabled)
            return Ok(new { enabled = false, reply = "The assistant isn't available right now." });

        if (request.Messages is null || request.Messages.Count == 0)
            return BadRequest(new { error = "Send a message to start." });

        // Guardrails: cap history length and per-message size, drop empties.
        var msgs = request.Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(20)
            .Select(m => (Role: m.Role == "assistant" ? "assistant" : "user",
                          Content: m.Content.Length > 2000 ? m.Content[..2000] : m.Content))
            .ToList();

        if (msgs.Count == 0 || msgs[^1].Role != "user")
            return BadRequest(new { error = "The last message must be from the user." });

        try
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var reply = await chat.ReplyAsync(msgs, userId, ct);
            return Ok(new { enabled = true, reply });
        }
        catch
        {
            return StatusCode(502, new { error = "The assistant is temporarily unavailable. Please try again." });
        }
    }
}
