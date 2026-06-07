namespace BlueberryMart.Api.Configuration;

/// <summary>
/// Settings for the customer support assistant, bound from the <c>"Chat"</c> section.
/// Empty <see cref="ApiKey"/> ⇒ the assistant is disabled (the endpoint reports
/// <c>enabled:false</c> and the chat tab shows an "unavailable" state).
/// </summary>
public class ChatOptions
{
    public string? ApiKey { get; set; }

    /// <summary>Any OpenAI-compatible chat-completions endpoint. Default: Groq (free tier).</summary>
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";

    /// <summary>
    /// Model id for the chosen provider. Examples:
    /// Groq <c>llama-3.3-70b-versatile</c>;
    /// Gemini <c>gemini-2.0-flash</c> (BaseUrl <c>https://generativelanguage.googleapis.com/v1beta/openai/chat/completions</c> — note: paid tier, not free in some regions);
    /// OpenRouter <c>meta-llama/llama-3.3-70b-instruct:free</c>; Ollama <c>llama3.2</c>.
    /// </summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    public int MaxTokens { get; set; } = 600;
}
