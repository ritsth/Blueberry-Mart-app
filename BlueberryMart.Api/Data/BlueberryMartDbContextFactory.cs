using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlueberryMart.Api.Data;

/// <summary>
/// Design-time factory used by the `dotnet ef` tooling so migration commands
/// can build the DbContext without running the full application host.
/// The connection string here is only used for commands that touch the database
/// (e.g. `database update`); `migrations add` does not connect.
/// </summary>
public class BlueberryMartDbContextFactory : IDesignTimeDbContextFactory<BlueberryMartDbContext>
{
    public BlueberryMartDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=blueberry_mart;Username=postgres;Password=ritsth";

        var options = new DbContextOptionsBuilder<BlueberryMartDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BlueberryMartDbContext(options);
    }
}
