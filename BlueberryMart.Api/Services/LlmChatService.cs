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
/// Customer support assistant backed by any <b>OpenAI-compatible</b> chat-completions
/// endpoint — so you can use a free/cheap provider (Groq, Google Gemini, OpenRouter,
/// a local Ollama, OpenAI, …) just by setting <c>Chat:BaseUrl</c>, <c>Chat:Model</c>
/// and <c>Chat:ApiKey</c>. The system prompt scopes the bot to Blueberry Mart items +
/// support and injects a live catalog snapshot so answers stay grounded.
/// </summary>
public sealed class LlmChatService : IChatService
{
    private readonly HttpClient _http;
    private readonly ChatOptions _opts;
    private readonly BlueberryMartDbContext _db;

    public bool Enabled => true;

    public LlmChatService(HttpClient http, IOptions<ChatOptions> opts, BlueberryMartDbContext db)
    {
        _http = http;
        _opts = opts.Value;
        _db = db;
    }

    public async Task<string> ReplyAsync(IReadOnlyList<(string Role, string Content)> messages, Guid userId, CancellationToken ct = default)
    {
        var system = await BuildSystemPromptAsync(userId, ct);

        // OpenAI-compatible: the system prompt is the first message with role "system".
        var chatMessages = new List<object> { new { role = "system", content = system } };
        foreach (var m in messages)
            chatMessages.Add(new { role = m.Role == "assistant" ? "assistant" : "user", content = m.Content });

        var payload = new
        {
            model = _opts.Model,
            max_tokens = _opts.MaxTokens,
            messages = chatMessages,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseUrl);
        req.Headers.Add("Authorization", $"Bearer {_opts.ApiKey}");
        req.Content = JsonContent.Create(payload);

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Assistant API error {(int)res.StatusCode}: {body}");
        }

        using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content) ? "Sorry, I couldn't come up with a reply." : content;
    }

    private async Task<string> BuildSystemPromptAsync(Guid userId, CancellationToken ct)
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

        var ordersText = await BuildOrdersTextAsync(userId, ct);

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
            "Current catalog:\n" + catalog + "\n\n" +
            "This customer's recent orders (most recent first; ONLY this customer's orders — never reveal anyone " +
            "else's). Status meanings: pending = placed but not paid yet; confirmed = paid & being prepared / ready; " +
            "completed = received; cancelled = cancelled. Use these to answer \"where's my order #N\" questions; if an " +
            "order number isn't in this list, say you don't see it on their account.\n" + ordersText;
    }

    private async Task<string> BuildOrdersTextAsync(Guid userId, CancellationToken ct)
    {
        var orders = await _db.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.OrderType,
                o.Status,
                o.TotalAmount,
                o.CreatedAt,
                Branch = o.Branch.Name,
                PaymentStatus = _db.Payments.Where(p => p.OrderId == o.Id).Select(p => p.Status).FirstOrDefault(),
            })
            .ToListAsync(ct);

        if (orders.Count == 0) return "(This customer has no orders yet.)";

        var orderIds = orders.Select(o => o.Id).ToList();
        var itemsByOrder = (await _db.OrderItems
                .Where(oi => orderIds.Contains(oi.OrderId))
                .Select(oi => new { oi.OrderId, oi.Item.ItemName, oi.Quantity })
                .ToListAsync(ct))
            .GroupBy(x => x.OrderId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => $"{x.ItemName} x{x.Quantity}")));

        var sb = new StringBuilder();
        foreach (var o in orders)
        {
            var pay = string.IsNullOrEmpty(o.PaymentStatus) ? "no payment yet" : o.PaymentStatus;
            var its = itemsByOrder.GetValueOrDefault(o.Id, "");
            sb.AppendLine(
                $"  - Order #{o.OrderNumber}: {o.CreatedAt:yyyy-MM-dd}, {o.Branch}, {o.OrderType}, " +
                $"status {o.Status}, payment {pay}, total Rs {o.TotalAmount:0.##}. Items: {its}");
        }
        return sb.ToString();
    }
}

/// <summary>Used when no API key is configured — the assistant reports as unavailable.</summary>
public sealed class DisabledChatService : IChatService
{
    public bool Enabled => false;

    public Task<string> ReplyAsync(IReadOnlyList<(string Role, string Content)> messages, Guid userId, CancellationToken ct = default)
        => throw new InvalidOperationException("The assistant is not configured.");
}
