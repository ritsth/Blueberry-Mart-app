using BlueberryMart.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Data;

public static class DbInitializer
{
    public static void Initialize(BlueberryMartDbContext context)
    {
        // Apply any pending EF Core migrations (creates the schema on a fresh DB,
        // brings an existing DB up to date). Replaces the old EnsureCreated().
        context.Database.Migrate();

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

        var users = new List<User>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Email = "customer1@blueberrymart.com",
                PasswordHash = BCrypt("customer1_password"),
                Role = "customer",
                LoyaltyPoints = 320,
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
        };

        context.Users.AddRange(users);
        context.SaveChanges();
    }

    // Placeholder — replace with a real BCrypt library (e.g. BCrypt.Net-Next) before production.
    private static string BCrypt(string plaintext) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(plaintext)));
}
