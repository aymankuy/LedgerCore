namespace LedgerCore.Infrastructure.Persistence.Seeding;

/// <summary>
/// Startup database work, bound from the "Database" section. Everything is off by default:
/// docker compose switches it on, while a cloud deploy runs migrations from its pipeline.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations and seed reference data when the API starts.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Connection with DDL rights, used only for migrations and for provisioning the
    /// application login. Falls back to the application connection when not set.
    /// </summary>
    public string? MigrationConnectionString { get; set; }

    /// <summary>
    /// When set, a least-privilege SQL login of this name is created for the application:
    /// read, write and execute, but no ALTER and no UPDATE or DELETE on ledger entries.
    /// </summary>
    public string? AppLogin { get; set; }

    public string? AppLoginPassword { get; set; }

    /// <summary>
    /// Currencies that get a settlement account, so deposits and withdrawals work, and a fee
    /// revenue account, so transfers can be charged a fee.
    /// </summary>
    public string[] SettlementCurrencies { get; set; } = ["EUR", "USD", "TRY"];

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    /// <summary>Two demo users with funded EUR accounts, for trying the API from Swagger.</summary>
    public bool SeedDemoData { get; set; }

    public string? DemoPassword { get; set; }
}
