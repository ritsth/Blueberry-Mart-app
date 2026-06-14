using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Data;

/// <summary>
/// Generates production-like demo data — customers and backdated orders (with line
/// items and payments) spread over a date range — so the portal's Reports, Dashboard,
/// and Orders screens show realistic numbers. Invoked via the `seed` CLI arg, never on
/// a normal startup. Seeded customers use the @seed.blueberrymart.com email domain so
/// they can be cleared with `seed clear`.
/// </summary>
public static class DataSeeder
{
    private const string SeedDomain = "@seed.blueberrymart.com";
    private const decimal MemberDiscountRate = 0.05m;
    private const decimal DeliveryFee = 100m;

    private static readonly string[] FirstNames =
        ["aarav",
            "diya",
            "kiran",
            "maya",
            "rohan",
            "sita",
            "arjun",
            "nisha",
            "raj",
            "priya",
            "sanjay",
            "anita",
            "bikash",
            "pooja",
            "deepak",
            "rita",
            "manish",
            "sneha",
            "ramesh",
            "gita"];
    private static readonly string[] LastNames =
        ["sharma", "thapa", "gurung", "rai", "shrestha", "magar", "karki", "adhikari", "khadka", "basnet"];

    // (name, price, isBulkOnly)
    private static readonly (string Name, decimal Price, bool Bulk)[] Catalogue =
    [
        ("Bananas (dozen)", 120, false),
        ("Whole Milk 1L", 110, false),
        ("Brown Eggs (12)", 220, false),
        ("Sourdough Bread", 95, false),
        ("Tomatoes 1kg", 80, false),
        ("Onions 1kg", 70, false),
        ("Chicken Breast 1kg", 420, false),
        ("Cheddar Cheese 250g", 360, false),
        ("Greek Yogurt 500g", 180, false),
        ("Orange Juice 1L", 240, false),
        ("Ground Coffee 250g", 540, false),
        ("Pasta 500g", 130, false),
        ("Apples 1kg", 200, false),
        ("Potatoes 2kg", 150, false),
        ("Butter 200g", 260, false),
        ("Breakfast Cereal 500g", 380, false),
        ("Dark Chocolate 100g", 150, false),
        ("Sparkling Water 6pk", 420, false),
        ("Basmati Rice 25kg", 3200, true),
        ("Cooking Oil 5L", 1450, true),
        ("Wheat Flour 10kg", 950, true),
    ];

    private static readonly (string Status, int Weight)[] StatusWeights =
    [
        ("completed", 58),
        ("ready", 8),
        ("processing", 8),
        ("confirmed", 9),
        ("pending", 9),
        ("cancelled", 8),
    ];

    public static async Task RunAsync(BlueberryMartDbContext ctx, string[] args)
    {
        if (args.Contains("clear"))
        {
            await ClearAsync(ctx);
            return;
        }

        var rnd = new Random();
        var customerCount = ArgInt(args, "--customers", 40);
        var orderCount = ArgInt(args, "--orders", 300);
        var days = ArgInt(args, "--days", 120);

        var branches = await ctx.Branches.Where(b => b.IsActive).ToListAsync();
        if (branches.Count == 0)
        {
            Console.WriteLine("No active branches found — run the app once to seed base data first.");
            return;
        }

        await EnsureCatalogueAsync(ctx, branches, rnd);
        var itemsByBranch = (await ctx.Inventory.Where(i => i.IsActive).ToListAsync())
            .GroupBy(i => i.BranchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ---- Customers ----
        var customers = new List<User>(customerCount);
        for (var i = 0; i < customerCount; i++)
        {
            var name = $"{Pick(rnd, FirstNames)}.{Pick(rnd, LastNames)}";
            var created = DateTime.UtcNow.AddDays(-rnd.Next(days, days + 240));
            var isMember = rnd.NextDouble() < 0.25;
            customers.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = $"{name}.{i}{SeedDomain}",
                PasswordHash = Hash("seed_password"),
                Role = "customer",
                LoyaltyPoints = 0,
                MemberSince = isMember ? created : null,
                MemberUntil = isMember ? DateTime.UtcNow.AddDays(rnd.Next(5, 60)) : null,
                CreatedAt = created,
                UpdatedAt = created,
            });
        }
        ctx.Users.AddRange(customers);
        await ctx.SaveChangesAsync();

        // ---- Orders (backdated, with items + payments) ----
        var paid = 0;
        for (var i = 0; i < orderCount; i++)
        {
            var customer = customers[rnd.Next(customers.Count)];
            var branch = branches[rnd.Next(branches.Count)];
            var catalogue = itemsByBranch[branch.Id];

            var orderId = Guid.NewGuid();
            var lineCount = rnd.Next(1, 5);
            var picks = catalogue.OrderBy(_ => rnd.Next()).Take(lineCount).ToList();

            decimal subtotal = 0;
            foreach (var item in picks)
            {
                var qty = rnd.Next(1, item.IsBulkOnly ? 4 : 6);
                subtotal += item.Price * qty;
                ctx.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    ItemId = item.Id,
                    Quantity = qty,
                    UnitPrice = item.Price,
                });
            }

            var isMember = customer.MemberUntil.HasValue;
            var discount = isMember ? Math.Round(subtotal * MemberDiscountRate, 2) : 0m;
            var type = rnd.NextDouble() < 0.4 ? "delivery" : "pickup";
            var deliveryFee = type == "delivery" ? (isMember ? 0m : DeliveryFee) : 0m;
            var total = subtotal - discount + deliveryFee;
            var status = WeightedStatus(rnd);
            var createdAt = DateTime.UtcNow
                .AddDays(-rnd.Next(0, days))
                .AddHours(-rnd.Next(0, 24))
                .AddMinutes(-rnd.Next(0, 60));

            ctx.Orders.Add(new Order
            {
                Id = orderId,
                UserId = customer.Id,
                BranchId = branch.Id,
                OrderType = type,
                Status = status,
                TotalAmount = total,
                DiscountAmount = discount,
                DeliveryFee = deliveryFee,
                DeliveryAddress = type == "delivery" ? $"Seeded address, {branch.LocationCity}" : null,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            });

            // Anything past 'pending'/'cancelled' has been paid; credit loyalty like the real flow.
            if (status is not "pending" and not "cancelled")
            {
                ctx.Payments.Add(new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    TransactionUuid = $"seed-{Guid.NewGuid()}",
                    Amount = total,
                    Status = "completed",
                    ProviderRef = rnd.NextDouble() < 0.5 ? "manual:cash" : "esewa",
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt,
                });
                customer.LoyaltyPoints += (int)Math.Floor(subtotal - discount);
                paid++;
            }
        }

        await ctx.SaveChangesAsync();
        Console.WriteLine($"Seeded {customerCount} customers and {orderCount} orders ({paid} paid) across {branches.Count} branch(es), over the last {days} days.");
    }

    private static async Task EnsureCatalogueAsync(BlueberryMartDbContext ctx, List<Branch> branches, Random rnd)
    {
        var existing = await ctx.Inventory
            .Select(i => new { i.BranchId, i.ItemName })
            .ToListAsync();
        var have = existing.Select(e => (e.BranchId, e.ItemName)).ToHashSet();

        var added = 0;
        foreach (var branch in branches)
        {
            foreach (var (name, price, bulk) in Catalogue)
            {
                if (have.Contains((branch.Id, name))) continue;
                ctx.Inventory.Add(new Inventory
                {
                    Id = Guid.NewGuid(),
                    BranchId = branch.Id,
                    ItemName = name,
                    Price = price,
                    StockQuantity = rnd.Next(15, 200),
                    IsBulkOnly = bulk,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
                added++;
            }
        }
        if (added > 0) await ctx.SaveChangesAsync();
        Console.WriteLine($"Catalogue: added {added} item(s) (existing items left as-is).");
    }

    private static async Task ClearAsync(BlueberryMartDbContext ctx)
    {
        var seedUserIds = await ctx.Users
            .Where(u => u.Email != null && u.Email.EndsWith(SeedDomain))
            .Select(u => u.Id)
            .ToListAsync();
        var orderIds = await ctx.Orders
            .Where(o => o.UserId != null && seedUserIds.Contains(o.UserId.Value))
            .Select(o => o.Id)
            .ToListAsync();

        // Delete dependents first (FK Restrict), then orders, then users (addresses/
        // notifications/subscriptions cascade at the DB level).
        await ctx.Payments.Where(p => orderIds.Contains(p.OrderId)).ExecuteDeleteAsync();
        await ctx.Reviews.Where(r => orderIds.Contains(r.OrderId)).ExecuteDeleteAsync();
        await ctx.OrderItems.Where(oi => orderIds.Contains(oi.OrderId)).ExecuteDeleteAsync();
        await ctx.Orders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        await ctx.Users.Where(u => seedUserIds.Contains(u.Id)).ExecuteDeleteAsync();

        Console.WriteLine($"Cleared {seedUserIds.Count} seeded customers and {orderIds.Count} orders. (Seeded catalogue items are left in place.)");
    }

    private static string WeightedStatus(Random rnd)
    {
        var total = StatusWeights.Sum(s => s.Weight);
        var roll = rnd.Next(total);
        var acc = 0;
        foreach (var (status, weight) in StatusWeights)
        {
            acc += weight;
            if (roll < acc) return status;
        }
        return "completed";
    }

    private static int ArgInt(string[] args, string name, int fallback)
    {
        var idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var v) ? v : fallback;
    }

    private static T Pick<T>(Random rnd, T[] items) => items[rnd.Next(items.Length)];

    private static string Hash(string plaintext) => PasswordHasher.Hash(plaintext);
}
