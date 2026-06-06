namespace BlueberryMart.Api.Services.Interfaces;

/// <summary>
/// The in-app customer support assistant. Answers only questions about Blueberry Mart
/// items and customer issues/support, grounded in the live catalog.
/// </summary>
public interface IChatService
{
    /// <summary><c>false</c> when no API key is configured (e.g. production today).</summary>
    bool Enabled { get; }

    Task<string> ReplyAsync(IReadOnlyList<(string Role, string Content)> messages, CancellationToken ct = default);
}
