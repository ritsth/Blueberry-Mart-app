using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Sends transactional email through Resend's HTTP API (<c>POST https://api.resend.com/emails</c>).
/// Registered via <c>AddHttpClient</c> only when <see cref="EmailOptions.ApiKey"/> is set; otherwise
/// the app uses <see cref="LoggingEmailSender"/>.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(IOptions<EmailOptions> options, HttpClient http, ILogger<ResendEmailSender> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var from = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromAddress
            : $"{_options.FromName} <{_options.FromAddress}>";

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new { from, to, subject, html = htmlBody })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Don't leak the recipient/body into logs; surface enough to debug deliverability.
            var detail = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend email send failed ({Status}): {Detail}", (int)resp.StatusCode, detail);
            throw new InvalidOperationException($"Email send failed with status {(int)resp.StatusCode}.");
        }
    }
}
