using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BlueberryMart.Api.Services.Interfaces;

namespace BlueberryMart.Api.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmailSender"/>. Captures the last email per recipient (in memory)
/// so tests can complete verification/reset flows — the link tokens are hashed in the DB and can't
/// be read back, so the plaintext is recovered from the captured email body instead.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    private static readonly Regex LinkParams =
        new(@"[?&]uid=(?<uid>[0-9a-fA-F-]{36})&t=(?<t>[^""&\s<]+)", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, string> _lastByRecipient = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        _lastByRecipient[to] = htmlBody;
        return Task.CompletedTask;
    }

    /// <summary>The uid + secret from the most recent email's link for <paramref name="to"/>, or null.</summary>
    public (Guid Uid, string Token)? LastLink(string to)
    {
        if (!_lastByRecipient.TryGetValue(to, out var html)) return null;
        var m = LinkParams.Match(html);
        if (!m.Success) return null;
        return (Guid.Parse(m.Groups["uid"].Value), Uri.UnescapeDataString(m.Groups["t"].Value));
    }

    public bool HasEmailFor(string to) => _lastByRecipient.ContainsKey(to);
}
