namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// Sends a single transactional email. Implemented by <c>ResendEmailSender</c> in production and by
/// <c>LoggingEmailSender</c> (logs instead of sends) when no provider is configured (local + tests).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
