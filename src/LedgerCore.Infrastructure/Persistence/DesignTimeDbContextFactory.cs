using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LedgerCore.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling (migrations add, database update). The application
/// itself gets its options from DI. Set LEDGERCORE_DESIGN_CONNECTION to target a real database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    // No credentials here on purpose: "migrations add" never connects, so a placeholder is enough.
    private const string PlaceholderConnection =
        "Server=localhost,14333;Database=LedgerCore;TrustServerCertificate=True";

    public LedgerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LEDGERCORE_DESIGN_CONNECTION")
            ?? PlaceholderConnection;

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new LedgerDbContext(options);
    }
}
