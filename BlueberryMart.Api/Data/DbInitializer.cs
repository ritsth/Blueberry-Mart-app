using BlueberryMart.Api.Models.Entities;
using BlueberryMart.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace BlueberryMart.Api.Data;

public static class DbInitializer
{
    /// <summary>Email of the former "Walk-in" system customer — kept only so the transitional
    /// cleanup below can remove it. In-store walk-in sales now use a null <c>Order.UserId</c>.</summary>
    private const string LegacyWalkInEmail = "walkin@system.blueberrymart.local";

    /// <summary>Demo login accounts previously seeded in every environment, including Production,
    /// with passwords published in project docs. See <see cref="RotateLegacyDemoAccountPasswords"/>.</summary>
    private static readonly string[] LegacyDemoAccountEmails =
    [
        "customer1@blueberrymart.com",
        "customer2@blueberrymart.com",
        "shareholder1@blueberrymart.com"
    ];

    public static void Initialize(BlueberryMartDbContext context, IConfiguration config, IHostEnvironment env)
    {
        // Apply any pending EF Core migrations (creates the schema on a fresh DB,
        // brings an existing DB up to date). Replaces the old EnsureCreated().
        context.Database.Migrate();

        SeedDemoData(context, env);
        EnsureAdmin(context, config);
        RemoveLegacyWalkInCustomer(context);
        RotateLegacyDemoAccountPasswords(context, env);
        EnsureSettings(context);
    }

    /// <summary>
    /// Transitional cleanup: drops the old "Walk-in" system customer that an earlier build seeded
    /// to carry anonymous in-store sales. Walk-in sales now use a null <c>Order.UserId</c>, so the
    /// fake account is no longer needed. Idempotent and safe — only deletes it if no orders reference
    /// it (they shouldn't, since the booking path changed before this ran).
    /// </summary>
    private static void RemoveLegacyWalkInCustomer(BlueberryMartDbContext context)
    {
        var walkIn = context.Users.FirstOrDefault(u => u.Email == LegacyWalkInEmail);
        if (walkIn is null) return;
        if (context.Orders.Any(o => o.UserId == walkIn.Id)) return;   // shouldn't happen; don't orphan data

        context.Users.Remove(walkIn);
        context.SaveChanges();
    }

    /// <summary>Ensures the single store-settings row exists (with the former hardcoded defaults).</summary>
    private static void EnsureSettings(BlueberryMartDbContext context)
    {
        if (context.StoreSettings.Any()) return;
        context.StoreSettings.Add(new StoreSettings { UpdatedAt = DateTime.UtcNow });
        context.SaveChanges();
    }

    /// <summary>
    /// Ensures a single bootstrap admin exists. Runs on every startup but is a no-op
    /// once an admin is present. Credentials come from the Admin config section
    /// (Secret Manager in prod, gitignored dev settings locally).
    /// </summary>
    private static void EnsureAdmin(BlueberryMartDbContext context, IConfiguration config)
    {
        if (context.Users.Any(u => u.Role == "admin")) return;

        var email = config["Admin:Email"]?.Trim().ToLower();
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        var existing = context.Users.FirstOrDefault(u => u.Email == email);
        if (existing is not null)
        {
            // Promote an existing account rather than failing on the unique email.
            existing.Role = "admin";
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = BCrypt(password),
                Role = "admin",
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        context.SaveChanges();
    }

    private static void SeedDemoData(BlueberryMartDbContext context, IHostEnvironment env)
    {
        if (context.Branches.Any() || context.Users.Any()) return;

        var downtown = new Branch
        {
            Id = Guid.NewGuid(),
            Name = "Blueberry Mart Downtown",
            LocationCity = "Kathmandu",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var suburbs = new Branch
        {
            Id = Guid.NewGuid(),
            Name = "Blueberry Mart Suburbs",
            LocationCity = "Lalitpur",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Branches.AddRange(downtown, suburbs);
        context.SaveChanges();

        var inventory = new List<Inventory>
        {
            // Downtown — regular items
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Whole Milk (1L)",        StockQuantity = 120, Price = 95.00m,   IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Brown Eggs (12 pack)",   StockQuantity = 60,  Price = 180.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Sourdough Bread",        StockQuantity = 35,  Price = 220.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Organic Spinach (250g)", StockQuantity = 0,   Price = 110.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            // Downtown — bulk-only items
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Basmati Rice (25kg)",    StockQuantity = 40,  Price = 3200.00m, IsBulkOnly = true,  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Sunflower Oil (20L)",    StockQuantity = 0,   Price = 4100.00m, IsBulkOnly = true,  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = downtown.Id, ItemName = "Lentils Mixed (10kg)",   StockQuantity = 55,  Price = 1450.00m, IsBulkOnly = true,  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

            // Suburbs — regular items
            new() { Id = Guid.NewGuid(), BranchId = suburbs.Id, ItemName = "Greek Yogurt (500g)",    StockQuantity = 80,  Price = 145.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = suburbs.Id, ItemName = "Cheddar Cheese (200g)",  StockQuantity = 0,   Price = 260.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = suburbs.Id, ItemName = "Free-Range Chicken (1kg)",StockQuantity = 45, Price = 480.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = suburbs.Id, ItemName = "Orange Juice (1L)",       StockQuantity = 90, Price = 130.00m,  IsBulkOnly = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            // Suburbs — bulk-only items
            new() { Id = Guid.NewGuid(), BranchId = suburbs.Id, ItemName = "Wheat Flour (50kg)",     StockQuantity = 30,  Price = 2800.00m, IsBulkOnly = true,  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), BranchId = suburbs.Id, ItemName = "Sugar (20kg)",            StockQuantity = 0,   Price = 1900.00m, IsBulkOnly = true,  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        };

        context.Inventory.AddRange(inventory);
        context.SaveChanges();

        // Demo login accounts are a local/dev/CI convenience only. Never seed them in Production —
        // a known email+password (one of them Shareholder-level) would grant real access to a
        // live system. Admins/testers create real accounts through normal registration instead.
        if (env.IsProduction()) return;

        var users = new List<User>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Email = "customer1@blueberrymart.com",
                PasswordHash = BCrypt("customer1_password"),
                Role = "customer",
                LoyaltyPoints = 320,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Email = "customer2@blueberrymart.com",
                PasswordHash = BCrypt("customer2_password"),
                Role = "customer",
                LoyaltyPoints = 0,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Email = "shareholder1@blueberrymart.com",
                PasswordHash = BCrypt("shareholder1_password"),
                Role = "shareholder",
                LoyaltyPoints = 0,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
        };

        context.Users.AddRange(users);
        context.SaveChanges();
    }

    /// <summary>
    /// Earlier deploys seeded the demo accounts in <see cref="LegacyDemoAccountEmails"/> into
    /// Production too, with passwords that were published in project docs. Production no longer
    /// seeds them (see <see cref="SeedDemoData"/>), but any copies an earlier deploy already
    /// created still have the old, now-public password. On every Production startup, overwrite
    /// their hash with a fresh random value that is never logged or stored anywhere — the
    /// published passwords stop working immediately. No-op once the accounts are gone, and
    /// a no-op everywhere but Production.
    /// </summary>
    private static void RotateLegacyDemoAccountPasswords(BlueberryMartDbContext context, IHostEnvironment env)
    {
        if (!env.IsProduction()) return;

        var accounts = context.Users.Where(u => LegacyDemoAccountEmails.Contains(u.Email)).ToList();
        if (accounts.Count == 0) return;

        foreach (var account in accounts)
        {
            account.PasswordHash = BCrypt(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            account.UpdatedAt = DateTime.UtcNow;
        }
        context.SaveChanges();
    }

    private static string BCrypt(string plaintext) => PasswordHasher.Hash(plaintext);
}
