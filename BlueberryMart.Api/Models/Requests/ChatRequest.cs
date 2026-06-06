namespace BlueberryMart.Api.Models.Requests;

/// <summary>A turn of the support-chat conversation sent from the app.</summary>
public class ChatRequest
{
    public List<ChatTurn> Messages { get; set; } = new();
}

public class ChatTurn
{
    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
