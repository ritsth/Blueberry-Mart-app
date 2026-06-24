using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Fallback email sender used when no provider (Resend) is configured — local dev and tests. It does
/// not send anything, and deliberately does NOT log the recipient address (PII) or the message body
/// (which carries the single-use verification/reset link), so no sensitive data is ever written to
/// logs. To exercise real delivery locally, set <c>Email:ApiKey</c> to a Resend key — Resend can send
/// to your own account address without a verified domain.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Only the constant, non-sensitive subject is logged — never the address or the link/token.
        logger.LogInformation(
            "Email provider not configured (Email:ApiKey is empty); \"{Subject}\" was not sent.", subject);
        return Task.CompletedTask;
    }
}
