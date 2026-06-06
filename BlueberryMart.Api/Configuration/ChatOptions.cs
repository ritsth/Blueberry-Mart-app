namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Settings for the customer support assistant, bound from the <c>"Chat"</c> section.
/// Empty <see cref="ApiKey"/> ⇒ the assistant is disabled (the endpoint reports
/// <c>enabled:false</c> and the chat tab shows an "unavailable" state).
/// </summary>
public class ChatOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-4-5-20251001";
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public int MaxTokens { get; set; } = 600;
}
