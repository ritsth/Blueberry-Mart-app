using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Customer support assistant backed by the Anthropic Messages API. The system prompt
/// scopes the bot to Blueberry Mart items + support and injects a live catalog snapshot
/// so answers stay grounded.
/// </summary>
public sealed class ClaudeChatService : IChatService
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;
    private readonly BlueberryMartDbContext _db;

    public bool Enabled => true;

    public ClaudeChatService(HttpClient http, IOptions<ChatOptions> opts, BlueberryMartDbContext db)
    {
        _http = http;
        _opts = opts.Value;
        _db = db;
    }

    public async Task<string> ReplyAsync(IReadOnlyList<(string Role, string Content)> messages, CancellationToken ct = default)
    {
        var system = await BuildSystemPromptAsync(ct);

        var payload = new
        {
            model = _opts.Model,
            max_tokens = _opts.MaxTokens,
            system,
            messages = messages.Select(m => new
            {
                role = m.Role == "assistant" ? "assistant" : "user",
                content = m.Content,
            }).ToArray(),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseUrl);
        req.Headers.Add("x-api-key", _opts.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = JsonContent.Create(payload);

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Assistant API error {(int)res.StatusCode}: {body}");
        }

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // content is an array of blocks; concatenate any text blocks.
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                sb.Append(block.GetProperty("text").GetString());
        }
        return sb.Length > 0 ? sb.ToString() : "Sorry, I couldn't come up with a reply.";
    }

    private async Task<string> BuildSystemPromptAsync(CancellationToken ct)
    {
        var items = await _db.Inventory
            .Include(i => i.Branch)
            .OrderBy(i => i.Branch.Name).ThenBy(i => i.ItemName)
            .Select(i => new
            {
                i.ItemName,
                i.Price,
                i.StockQuantity,
                i.IsBulkOnly,
                Branch = i.Branch.Name,
                City = i.Branch.LocationCity,
            })
            .ToListAsync(ct);

        var catalog = new StringBuilder();
        foreach (var grp in items.GroupBy(i => new { i.Branch, i.City }))
        {
            catalog.AppendLine($"{grp.Key.Branch} ({grp.Key.City}):");
            foreach (var i in grp)
            {
                var stock = i.StockQuantity > 0 ? $"{i.StockQuantity} in stock" : "out of stock";
                var bulk = i.IsBulkOnly ? ", bulk-only (members)" : "";
                catalog.AppendLine($"  - {i.ItemName}: Rs {i.Price:0.##}, {stock}{bulk}");
            }
        }

        return
            "You are the Blueberry Mart shopping assistant — a friendly in-app helper for customers of the " +
            "Blueberry Mart grocery store (Nepal; prices in Rs / NPR).\n\n" +
            "You ONLY help with two things:\n" +
            "1. Questions about Blueberry Mart's items — availability, price, which branch has them, stock, and bulk options.\n" +
            "2. Customer issues & support — placing and paying for orders, pickup vs delivery, Blueberry Plus membership, " +
            "loyalty points, reviews, and \"how do I…\" help for the app.\n\n" +
            "If a question is outside these two topics (general knowledge, other stores, chit-chat, or anything unrelated to " +
            "Blueberry Mart shopping/support), politely decline in one sentence and offer to help with an item or an order issue instead.\n\n" +
            "Keep replies short, friendly and concrete. NEVER invent items, prices or stock — use only the catalog below. " +
            "If an item isn't listed, say Blueberry Mart doesn't carry it.\n\n" +
            "How the app works (for support answers):\n" +
            "- Order: pick a branch, add items, choose Pickup or Delivery, place the order, then pay with eSewa.\n" +
            "- Members get free delivery; non-members pay a flat Rs 100 delivery fee. Pickup is always free.\n" +
            "- After paying, the order is Confirmed; tap \"Mark as received\" when you get it, then you can leave a review.\n" +
            "- Blueberry Plus (Rs 199/month): 5% off every order, free delivery, and bulk ordering.\n" +
            "- Loyalty: 1 point per Rs of goods; reviews earn 10 points (20 with a photo).\n" +
            "- For an out-of-stock item, tap \"Notify me\" to be alerted when it's back.\n\n" +
            "Current catalog:\n" + catalog;
    }
}

/// <summary>Used when no API key is configured — the assistant reports as unavailable.</summary>
public sealed class DisabledChatService : IChatService
{
    public bool Enabled => false;

    public Task<string> ReplyAsync(IReadOnlyList<(string Role, string Content)> messages, CancellationToken ct = default)
        => throw new InvalidOperationException("The assistant is not configured.");
}
