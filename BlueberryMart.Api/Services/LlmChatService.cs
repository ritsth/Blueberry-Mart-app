using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlueberryMart.Api.Configuration;
using BlueberryMart.Api.Data;
using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlueberryMart.Api.Services;

/// <summary>
/// Customer support assistant backed by any <b>OpenAI-compatible</b> chat-completions
/// endpoint (Groq by default; Gemini/OpenRouter/Ollama/OpenAI via config).
///
/// Fully tool-driven: the model calls tools on demand for items and orders, executed
/// scoped strictly to the signed-in customer, so one customer can never see another's data.
/// Tools: <c>search_items</c> (catalog), <c>get_order</c> / <c>list_my_orders</c> (the
/// customer's orders), <c>subscribe_back_in_stock</c> (action).
/// </summary>
public sealed class LlmChatService : IChatService
{
    private const int MaxToolRounds = 5;

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
        var history = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = await BuildSystemPromptAsync(ct) } };
        foreach (var m in messages)
            history.Add(new JsonObject { ["role"] = m.Role == "assistant" ? "assistant" : "user", ["content"] = m.Content });

        // Tool-calling loop: call the model; if it asks for tools, run them (scoped to the
        // user), append the results, and call again — until it returns a final answer.
        for (var round = 0; round < MaxToolRounds; round++)
        {
            var body = new JsonObject
            {
                ["model"] = _opts.Model,
                ["max_tokens"] = _opts.MaxTokens,
                ["messages"] = history.DeepClone(),
                ["tools"] = BuildTools(),
            };

            var root = await PostAsync(body, ct);
            var message = root?["choices"]?[0]?["message"];
            if (message is null) return "Sorry, I couldn't come up with a reply.";

            var toolCalls = message["tool_calls"] as JsonArray;
            if (toolCalls is null || toolCalls.Count == 0)
                return message["content"]?.GetValue<string>() ?? "Sorry, I couldn't come up with a reply.";

            history.Add(JsonNode.Parse(message.ToJsonString())!);

            foreach (var call in toolCalls)
            {
                var fn = call?["function"];
                var name = fn?["name"]?.GetValue<string>() ?? "";
                var args = fn?["arguments"]?.GetValue<string>() ?? "{}";
                var id = call?["id"]?.GetValue<string>() ?? "";
                var result = await ExecuteToolAsync(name, args, userId, ct);
                history.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result });
            }
        }

        return "Sorry, that took too many steps — please try rephrasing.";
    }

    private async Task<JsonNode?> PostAsync(JsonObject body, CancellationToken ct)
    {
        var payload = body.ToJsonString();
        // Retry on Groq/Llama's intermittent "tool_use_failed" (a malformed tool call).
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseUrl);
            req.Headers.Add("Authorization", $"Bearer {_opts.ApiKey}");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode) return JsonNode.Parse(text);

            if (attempt < 2 && (int)res.StatusCode == 400 && text.Contains("tool_use_failed"))
                continue;
            throw new InvalidOperationException($"Assistant API error {(int)res.StatusCode}: {text}");
        }
    }

    // --- tools --------------------------------------------------------------------
    private static JsonArray BuildTools() => new()
    {
        Tool("get_order",
            "Look up ONE of the signed-in customer's orders by its order number. Returns status, items, total, branch and "
            + "date — or found:false if that order isn't on this customer's account.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["order_number"] = new JsonObject { ["type"] = "integer", ["description"] = "The order number, e.g. 1042" },
                },
                ["required"] = new JsonArray { "order_number" },
            }),
        Tool("list_my_orders",
            "List the signed-in customer's recent orders (number, status, total, date, type).",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        Tool("subscribe_back_in_stock",
            "Subscribe the signed-in customer to a back-in-stock alert for an item that is currently out of stock. Use when "
            + "the customer asks to be notified when an item returns.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["item_name"] = new JsonObject { ["type"] = "string", ["description"] = "The item name, e.g. 'Organic Spinach'" },
                    ["branch"] = new JsonObject { ["type"] = "string", ["description"] = "Optional branch name to narrow it down" },
                },
                ["required"] = new JsonArray { "item_name" },
            }),
    };

    private static JsonObject Tool(string name, string description, JsonObject parameters) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = parameters },
    };

    private async Task<string> ExecuteToolAsync(string name, string argsJson, Guid userId, CancellationToken ct)
    {
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var root = args.RootElement;
            switch (name)
            {
                case "get_order":
                    {
                        if (!root.TryGetProperty("order_number", out var n))
                            return Json(new { error = "order_number is required" });
                        var num = n.ValueKind == JsonValueKind.Number ? n.GetInt32() : int.Parse(n.GetString() ?? "0");
                        return await GetOrderAsync(num, userId, ct);
                    }
                case "list_my_orders":
                    return await ListOrdersAsync(userId, ct);
                case "subscribe_back_in_stock":
                    return await SubscribeBackInStockAsync(Str(root, "item_name") ?? "", Str(root, "branch"), userId, ct);
                default:
                    return Json(new { error = $"unknown tool '{name}'" });
            }
        }
        catch (Exception ex)
        {
            return Json(new { error = "tool failed", detail = ex.Message });
        }
    }

    private static string? Str(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) ? v.GetString() : null;

    private async Task<string> GetOrderAsync(int orderNumber, Guid userId, CancellationToken ct)
    {
        var o = await _db.Orders
            .Where(x => x.UserId == userId && x.OrderNumber == orderNumber)
            .Select(x => new
            {
                x.Id,
                x.OrderNumber,
                x.OrderType,
                x.Status,
                x.TotalAmount,
                x.CreatedAt,
                Branch = x.Branch.Name,
                PaymentStatus = _db.Payments.Where(p => p.OrderId == x.Id).Select(p => p.Status).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        if (o is null)
            return Json(new { found = false, message = "No order with that number on this customer's account." });

        var items = await _db.OrderItems
            .Where(oi => oi.OrderId == o.Id)
            .Select(oi => new { name = oi.Item.ItemName, quantity = oi.Quantity })
            .ToListAsync(ct);

        return Json(new
        {
            found = true,
            order_number = o.OrderNumber,
            status = o.Status,
            type = o.OrderType,
            payment = o.PaymentStatus ?? "none",
            total = o.TotalAmount,
            date = o.CreatedAt.ToString("yyyy-MM-dd"),
            branch = o.Branch,
            items,
        });
    }

    private async Task<string> ListOrdersAsync(Guid userId, CancellationToken ct)
    {
        var raw = await _db.Orders
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(15)
            .Select(x => new { x.OrderNumber, x.Status, x.OrderType, x.TotalAmount, x.CreatedAt })
            .ToListAsync(ct);

        var orders = raw.Select(x => new
        {
            order_number = x.OrderNumber,
            status = x.Status,
            type = x.OrderType,
            total = x.TotalAmount,
            date = x.CreatedAt.ToString("yyyy-MM-dd"),
        });

        return Json(new { count = raw.Count, orders });
    }

    private async Task<string> SubscribeBackInStockAsync(string itemName, string? branch, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return Json(new { subscribed = false, message = "item_name is required" });

        var query = _db.Inventory.Include(i => i.Branch)
            .Where(i => EF.Functions.ILike(i.ItemName, $"%{itemName}%"));
        if (!string.IsNullOrWhiteSpace(branch))
            query = query.Where(i => EF.Functions.ILike(i.Branch.Name, $"%{branch}%"));

        var matches = await query
            .Select(i => new { i.Id, i.ItemName, i.StockQuantity, Branch = i.Branch.Name })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return Json(new { subscribed = false, message = "No matching item found in the catalog." });

        var outOfStock = matches.Where(m => m.StockQuantity <= 0).ToList();
        if (outOfStock.Count == 0)
            return Json(new { subscribed = false, message = "That item is currently in stock — no alert needed." });

        var done = new List<object>();
        foreach (var m in outOfStock)
        {
            var already = await _db.StockSubscriptions
                .AnyAsync(s => s.UserId == userId && s.InventoryId == m.Id && s.NotifiedAt == null, ct);
            if (!already)
                _db.StockSubscriptions.Add(new StockSubscription { UserId = userId, InventoryId = m.Id });
            done.Add(new { item = m.ItemName, branch = m.Branch, newly_subscribed = !already });
        }
        await _db.SaveChangesAsync(ct);

        return Json(new { subscribed = true, items = done });
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    // --- system prompt (catalog injected; orders & actions via tools) -------------
    // Item facts are injected so the common item path needs NO tool call (Groq/Llama
    // tool-calling is intermittently unreliable); search_items remains available as a tool.
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
            "2. Customer issues & support — orders, paying, pickup vs delivery, Blueberry Plus membership, loyalty, reviews.\n\n" +
            "If a question is outside these topics, politely decline in one sentence and offer to help with an item or an order.\n\n" +
            "Keep replies short, friendly and concrete.\n\n" +
            "ITEMS: item facts are in the CATALOG below — answer item questions (price, stock, branch, bulk) directly from it. " +
            "Never invent items, prices or stock; if something isn't listed, say Blueberry Mart doesn't carry it. " +
            "(A `search_items` tool is also available if you ever need a lookup.)\n\n" +
            "ORDERS: to answer anything about THIS customer's orders (status, contents, totals, \"where's my order #N\"), call " +
            "`get_order` or `list_my_orders`. They're scoped to the signed-in customer — never ask who they are or reveal " +
            "others' data, and base order answers only on tool results. Order status: pending = placed but not paid yet; " +
            "confirmed = paid & being prepared / ready; completed = received; cancelled = cancelled.\n\n" +
            "ACTIONS: if an item is out of stock and the customer wants to be told when it returns, call " +
            "`subscribe_back_in_stock` with the item name to sign them up.\n\n" +
            "How the app works (for support answers):\n" +
            "- Order: pick a branch, add items, choose Pickup or Delivery, place the order, then pay with eSewa.\n" +
            "- Members get free delivery; non-members pay a flat Rs 100 delivery fee. Pickup is always free.\n" +
            "- After paying, the order is Confirmed; tap \"Mark as received\" when you get it, then you can leave a review.\n" +
            "- Blueberry Plus (Rs 199/month): 5% off every order, free delivery, and bulk ordering.\n" +
            "- Loyalty: 1 point per Rs of goods; reviews earn 10 points (20 with a photo).\n" +
            "- For an out-of-stock item, tap \"Notify me\" (or just ask me) to be alerted when it's back.\n\n" +
            "Current catalog:\n" + catalog;
    }
}

/// <summary>Used when no API key is configured — the assistant reports as unavailable.</summary>
public sealed class DisabledChatService : IChatService
{
    public bool Enabled => false;

    public Task<string> ReplyAsync(IReadOnlyList<(string Role, string Content)> messages, Guid userId, CancellationToken ct = default)
        => throw new InvalidOperationException("The assistant is not configured.");
}
