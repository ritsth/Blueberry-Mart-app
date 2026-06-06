namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Settings for the customer support assistant, bound from the <c>"Chat"</c> section.
/// Empty <see cref="ApiKey"/> ⇒ the assistant is disabled (the endpoint reports
/// <c>enabled:false</c> and the chat tab shows an "unavailable" state).
/// </summary>
public class ChatOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Any OpenAI-compatible chat-completions endpoint. Default: Google Gemini (free tier).</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

    /// <summary>
    /// Model id for the chosen provider. Examples:
    /// Gemini <c>gemini-2.0-flash</c> (or <c>gemini-2.5-flash</c>);
    /// Groq <c>llama-3.3-70b-versatile</c> (BaseUrl <c>https://api.groq.com/openai/v1/chat/completions</c>);
    /// OpenRouter <c>meta-llama/llama-3.3-70b-instruct:free</c>; Ollama <c>llama3.2</c>.
    /// </summary>
    public string Model { get; set; } = "gemini-2.0-flash";

    public int MaxTokens { get; set; } = 600;
}
