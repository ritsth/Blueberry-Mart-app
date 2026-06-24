using BlueberryMart.Api.Data;
using BlueberryMart.Api.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlueberryMart.Api.Tests.Infrastructure;

public class BlueberryMartApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestConnectionString =
        "Host=localhost;Database=blueberry_mart_test;Username=postgres;Password=ritsth";

    public const string AdminEmail = "admin@blueberrymart.com";
    public const string AdminPassword = "admin_test_password";

    public Guid DowntownBranchId { get; private set; }
    public Guid SuburbsBranchId  { get; private set; }
    public Guid EggsItemId       { get; private set; }
    public Guid MilkItemId       { get; private set; }
    public Guid BreadItemId      { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Redirect uploaded images to a temp folder so tests don't depend on the source tree
        var testWebRoot = Path.Combine(Path.GetTempPath(), "blueberry_mart_test_wwwroot");
        Directory.CreateDirectory(Path.Combine(testWebRoot, "images", "reviews"));
        builder.UseWebRoot(testWebRoot);

        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestConnectionString,
                ["Jwt:Secret"] = "test-only-secret-key-not-used-in-production-32x",
                ["Admin:Email"] = AdminEmail,
                ["Admin:Password"] = AdminPassword,
                // The suite hammers the API from one loopback IP — raise every rate limit so it
                // never throttles. The throttling behavior itself is covered by dedicated tests
                // that lower these via WithWebHostBuilder.
                ["RateLimiting:Auth:PermitLimit"] = "100000",
                ["RateLimiting:Chat:PermitLimit"] = "100000",
                ["RateLimiting:Global:PermitLimit"] = "100000"
            }));

        builder.ConfigureServices(services =>
        {
            // Swap real Google token validation (calls Google) for a fake that parses test tokens.
            services.AddScoped<BlueberryMart.Api.Security.IGoogleTokenValidator, FakeGoogleTokenValidator>();

            // Capture emails instead of sending them, so tests can complete verification/reset flows.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());
        });
    }

    /// <summary>The in-memory email capture used to read verification/reset links in tests.</summary>
    public FakeEmailSender Emails => Services.GetRequiredService<FakeEmailSender>();

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BlueberryMartDbContext>();

        await context.Database.EnsureDeletedAsync();
        // DbInitializer.Initialize runs Migrate() to build the schema from migrations, then seeds
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        DbInitializer.Initialize(context, config);

        DowntownBranchId = (await context.Branches.FirstAsync(b => b.Name == "Blueberry Mart Downtown")).Id;
        SuburbsBranchId  = (await context.Branches.FirstAsync(b => b.Name == "Blueberry Mart Suburbs")).Id;
        EggsItemId       = (await context.Inventory.FirstAsync(i => i.ItemName == "Brown Eggs (12 pack)")).Id;
        MilkItemId       = (await context.Inventory.FirstAsync(i => i.ItemName == "Whole Milk (1L)")).Id;
        BreadItemId      = (await context.Inventory.FirstAsync(i => i.ItemName == "Sourdough Bread")).Id;
    }

    public new async Task DisposeAsync() => await base.DisposeAsync();
}
