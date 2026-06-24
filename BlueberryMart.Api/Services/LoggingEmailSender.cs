using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Fallback email sender used when no provider (Resend) is configured — local dev and tests.
/// Logs the message (including the verification/reset link) instead of sending, so the flow is
/// fully exercisable without an email account.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation("[email:dev] To={To} Subject=\"{Subject}\"\n{Body}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
