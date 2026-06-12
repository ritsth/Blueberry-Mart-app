using BlueberryMart.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlueberryMart.Api.Data;

public class BlueberryMartDbContext(DbContextOptions<BlueberryMartDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StockSubscription> StockSubscriptions => Set<StockSubscription>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum("user_role", ["customer", "shareholder"]);
        modelBuilder.HasPostgresEnum("order_type", ["pickup", "delivery"]);
        modelBuilder.HasPostgresEnum("order_status", ["pending", "confirmed", "processing", "ready", "completed", "cancelled"]);
        modelBuilder.HasPostgresEnum("payment_status", ["initiated", "completed", "failed"]);

        // Human-friendly sequential order numbers, starting at 1001
        modelBuilder.HasSequence<int>("order_number_seq").StartsAt(1001);

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(u => u.Role).HasColumnName("role").HasDefaultValue("customer");
            e.Property(u => u.BranchId).HasColumnName("branch_id");
            e.Property(u => u.LoyaltyPoints).HasColumnName("loyalty_points").HasDefaultValue(0);
            e.Property(u => u.MemberSince).HasColumnName("member_since");
            e.Property(u => u.MemberUntil).HasColumnName("member_until");
            e.Property(u => u.MembershipCancelled).HasColumnName("membership_cancelled").HasDefaultValue(false);
            e.Property(u => u.IsBanned).HasColumnName("is_banned").HasDefaultValue(false);
            e.Property(u => u.BannedAt).HasColumnName("banned_at");
            e.Property(u => u.BanReason).HasColumnName("ban_reason").HasMaxLength(500);
            e.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(u => u.Branch).WithMany().HasForeignKey(u => u.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.BranchId);
        });

        modelBuilder.Entity<StoreSettings>(e =>
        {
            e.ToTable("store_settings");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.DeliveryFee).HasColumnName("delivery_fee").HasColumnType("numeric(12,2)");
            e.Property(s => s.MembershipMonthlyFee).HasColumnName("membership_monthly_fee").HasColumnType("numeric(12,2)");
            e.Property(s => s.MemberDiscountRate).HasColumnName("member_discount_rate").HasColumnType("numeric(5,4)");
            e.Property(s => s.MaintenanceMode).HasColumnName("maintenance_mode").HasDefaultValue(false);
            e.Property(s => s.MaintenanceMessage).HasColumnName("maintenance_message").HasMaxLength(500);
            e.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("branches");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(b => b.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(b => b.LocationCity).HasColumnName("location_city").HasMaxLength(100).IsRequired();
            e.Property(b => b.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<Inventory>(e =>
        {
            e.ToTable("inventory");
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(i => i.BranchId).HasColumnName("branch_id");
            e.Property(i => i.ItemName).HasColumnName("item_name").HasMaxLength(200).IsRequired();
            e.Property(i => i.StockQuantity).HasColumnName("stock_quantity").HasDefaultValue(0);
            e.Property(i => i.Price).HasColumnName("price").HasColumnType("numeric(12,2)");
            e.Property(i => i.IsBulkOnly).HasColumnName("is_bulk_only").HasDefaultValue(false);
            e.Property(i => i.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(i => i.ImageUrl).HasColumnName("image_url");
            e.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(i => i.Branch).WithMany(b => b.Inventory).HasForeignKey(i => i.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.BranchId);
            e.HasIndex(i => new { i.BranchId, i.ItemName });
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(oi => oi.Id);
            e.Property(oi => oi.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(oi => oi.OrderId).HasColumnName("order_id");
            e.Property(oi => oi.ItemId).HasColumnName("item_id");
            e.Property(oi => oi.Quantity).HasColumnName("quantity");
            e.Property(oi => oi.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)");
            e.HasOne(oi => oi.Order).WithMany().HasForeignKey(oi => oi.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(oi => oi.Item).WithMany().HasForeignKey(oi => oi.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(oi => oi.OrderId);
            e.HasIndex(oi => oi.ItemId);
        });

        modelBuilder.Entity<Review>(e =>
        {
            e.ToTable("reviews");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.UserId).HasColumnName("user_id");
            e.Property(r => r.OrderId).HasColumnName("order_id");
            e.Property(r => r.ItemId).HasColumnName("item_id");
            e.Property(r => r.Rating).HasColumnName("rating");
            e.Property(r => r.Comment).HasColumnName("comment").IsRequired();
            e.Property(r => r.ImagePath).HasColumnName("image_path");
            e.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(r => r.User).WithMany(u => u.Reviews).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Order).WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Item).WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => r.ItemId);
            e.HasIndex(r => r.OrderId);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(o => o.OrderNumber).HasColumnName("order_number")
                .HasDefaultValueSql("nextval('order_number_seq')")
                .ValueGeneratedOnAdd();
            e.HasIndex(o => o.OrderNumber).IsUnique();
            e.Property(o => o.UserId).HasColumnName("user_id");
            e.Property(o => o.BranchId).HasColumnName("branch_id");
            e.Property(o => o.OrderType).HasColumnName("order_type").IsRequired();
            e.Property(o => o.Status).HasColumnName("status").HasDefaultValue("pending");
            e.Property(o => o.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)");
            e.Property(o => o.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").HasDefaultValue(0);
            e.Property(o => o.DeliveryAddress).HasColumnName("delivery_address");
            e.Property(o => o.DeliveryFee).HasColumnName("delivery_fee").HasColumnType("numeric(12,2)").HasDefaultValue(0);
            e.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(o => o.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(o => o.User).WithMany(u => u.Orders).HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Branch).WithMany(b => b.Orders).HasForeignKey(o => o.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => o.UserId);
            e.HasIndex(o => o.BranchId);
            e.HasIndex(o => o.Status);
            e.HasIndex(o => o.CreatedAt);
        });

        modelBuilder.Entity<Address>(e =>
        {
            e.ToTable("addresses");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(a => a.UserId).HasColumnName("user_id");
            e.Property(a => a.Label).HasColumnName("label").IsRequired();
            e.Property(a => a.AddressLine).HasColumnName("address_line").IsRequired();
            e.Property(a => a.City).HasColumnName("city").IsRequired();
            e.Property(a => a.Phone).HasColumnName("phone");
            e.Property(a => a.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
            e.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(a => a.User).WithMany(u => u.Addresses).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.UserId);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.OrderId).HasColumnName("order_id");
            e.Property(p => p.TransactionUuid).HasColumnName("transaction_uuid").IsRequired();
            e.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
            e.Property(p => p.Status).HasColumnName("status").HasDefaultValue("initiated");
            e.Property(p => p.ProviderRef).HasColumnName("provider_ref");
            e.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.OrderId).IsUnique();
            e.HasIndex(p => p.TransactionUuid).IsUnique();
        });

        modelBuilder.Entity<StockSubscription>(e =>
        {
            e.ToTable("stock_subscriptions");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.UserId).HasColumnName("user_id");
            e.Property(s => s.InventoryId).HasColumnName("inventory_id");
            e.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(s => s.NotifiedAt).HasColumnName("notified_at");
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Item).WithMany().HasForeignKey(s => s.InventoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.InventoryId);
            e.HasIndex(s => new { s.UserId, s.InventoryId });
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(n => n.UserId).HasColumnName("user_id");
            e.Property(n => n.Message).HasColumnName("message").IsRequired();
            e.Property(n => n.InventoryId).HasColumnName("inventory_id");
            e.Property(n => n.IsRead).HasColumnName("is_read").HasDefaultValue(false);
            e.Property(n => n.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => n.UserId);
        });

        modelBuilder.Entity<StockAdjustment>(e =>
        {
            e.ToTable("stock_adjustments");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(s => s.InventoryId).HasColumnName("inventory_id");
            e.Property(s => s.BranchId).HasColumnName("branch_id");
            e.Property(s => s.UserId).HasColumnName("user_id");
            e.Property(s => s.Delta).HasColumnName("delta");
            e.Property(s => s.NewQuantity).HasColumnName("new_quantity");
            e.Property(s => s.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
            e.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(s => s.Item).WithMany().HasForeignKey(s => s.InventoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.InventoryId);
            e.HasIndex(s => s.BranchId);
        });

        modelBuilder.Entity<SavedReport>(e =>
        {
            e.ToTable("saved_reports");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.ShareholderId).HasColumnName("shareholder_id");
            e.Property(r => r.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(r => r.ConfigJson).HasColumnName("config").HasColumnType("jsonb").IsRequired();
            e.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(r => r.Shareholder).WithMany().HasForeignKey(r => r.ShareholderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.ShareholderId);
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(o => o.Topic).HasColumnName("topic").IsRequired();
            e.Property(o => o.Key).HasColumnName("key").IsRequired();
            e.Property(o => o.Payload).HasColumnName("payload").IsRequired();
            e.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(o => o.PublishedAt).HasColumnName("published_at");
            e.Property(o => o.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            // The dispatcher scans for unpublished rows oldest-first.
            e.HasIndex(o => new { o.PublishedAt, o.CreatedAt });
        });
    }
}
