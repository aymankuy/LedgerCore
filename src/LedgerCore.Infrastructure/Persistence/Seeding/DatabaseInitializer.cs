using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Users;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LedgerCore.Infrastructure.Persistence.Seeding;

/// <summary>
/// Brings a database to a usable state: schema, application login, and the reference data
/// the API cannot work without. Every step is idempotent, so it is safe on every start.
/// </summary>
internal sealed class DatabaseInitializer(
    LedgerDbContext db,
    ILedgerRepository ledger,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly DatabaseOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // SQL Server in a container accepts connections a few seconds after it reports started.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await MigrateAsync(cancellationToken);
                break;
            }
            catch (SqlException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Database not reachable (attempt {Attempt}/{MaxAttempts}), retrying", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        await SeedReferenceDataAsync(cancellationToken);

        if (_options.SeedDemoData)
        {
            await SeedDemoDataAsync(cancellationToken);
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var connectionString = _options.MigrationConnectionString ?? db.Database.GetConnectionString();

        await using var admin = new LedgerDbContext(
            new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(connectionString).Options);

        await admin.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database schema is up to date");

        if (!string.IsNullOrWhiteSpace(_options.AppLogin) && !string.IsNullOrWhiteSpace(_options.AppLoginPassword))
        {
            await ProvisionAppLoginAsync(admin, _options.AppLogin, _options.AppLoginPassword, cancellationToken);
        }
    }

    // ADR 0001 relies on the application login lacking ALTER, because TRUNCATE bypasses triggers.
    // The DENY is a second line of defence behind the append-only trigger.
    private async Task ProvisionAppLoginAsync(
        LedgerDbContext admin, string login, string password, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
            BEGIN
                DECLARE @create nvarchar(max) =
                    N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = OFF';
                EXEC sp_executesql @create;
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login)
            BEGIN
                DECLARE @user nvarchar(max) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
                EXEC sp_executesql @user;
            END;

            DECLARE @grants nvarchar(max) =
                N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@login) + N';' +
                N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@login) + N';' +
                N'GRANT EXECUTE ON SCHEMA::dbo TO ' + QUOTENAME(@login) + N';' +
                N'GRANT EXECUTE ON TYPE::dbo.LedgerEntryList TO ' + QUOTENAME(@login) + N';' +
                N'DENY UPDATE, DELETE ON dbo.LedgerEntries TO ' + QUOTENAME(@login) + N';';
            EXEC sp_executesql @grants;
            """;

        await admin.Database.ExecuteSqlRawAsync(
            sql,
            [new SqlParameter("@login", login), new SqlParameter("@password", password)],
            cancellationToken);

        logger.LogInformation("Application login {Login} provisioned with least privilege", login);
    }

    private async Task SeedReferenceDataAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var system = await EnsureUserAsync(SeedUsers.SystemEmail, Guid.NewGuid().ToString("N"), UserRole.User, now, cancellationToken);

        foreach (var code in _options.SettlementCurrencies)
        {
            var currency = Currency.From(code);
            if (!await db.Accounts.AnyAsync(a => a.Kind == AccountKind.Settlement && a.Currency == currency, cancellationToken))
            {
                db.Accounts.Add(Account.OpenSettlement(system.Id, currency, now));
                logger.LogInformation("Seeded settlement account for {Currency}", code);
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.AdminEmail) && !string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            await EnsureUserAsync(_options.AdminEmail, _options.AdminPassword, UserRole.Admin, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDemoDataAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DemoPassword))
        {
            throw new InvalidOperationException("Database:DemoPassword is required when SeedDemoData is on.");
        }

        if (await db.Users.AnyAsync(u => u.Email == SeedUsers.DemoEmails[0], cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var eur = Currency.From("EUR");

        var alice = await EnsureUserAsync(SeedUsers.DemoEmails[0], _options.DemoPassword, UserRole.User, now, cancellationToken);
        var bob = await EnsureUserAsync(SeedUsers.DemoEmails[1], _options.DemoPassword, UserRole.User, now, cancellationToken);
        var aliceAccount = Account.Open(alice.Id, eur, now);
        var bobAccount = Account.Open(bob.Id, eur, now);
        db.Accounts.AddRange(aliceAccount, bobAccount);
        await db.SaveChangesAsync(cancellationToken);

        var settlement = await db.Accounts.SingleAsync(a => a.Kind == AccountKind.Settlement && a.Currency == eur, cancellationToken);

        await ledger.PostAsync(LedgerTransaction.Deposit(settlement, aliceAccount, 1_000m, now, description: "Demo opening deposit"), cancellationToken);
        await ledger.PostAsync(LedgerTransaction.Deposit(settlement, bobAccount, 250m, now, description: "Demo opening deposit"), cancellationToken);
        await ledger.PostAsync(LedgerTransaction.Transfer(aliceAccount, 1_000m, bobAccount, 75m, now, description: "Demo transfer"), cancellationToken);

        logger.LogInformation("Seeded demo users {Emails}", string.Join(", ", SeedUsers.DemoEmails));
    }

    private async Task<User> EnsureUserAsync(
        string email, string password, UserRole role, DateTime now, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = User.Register(email, passwordHasher.Hash(password), role, now);
        db.Users.Add(user);
        return user;
    }
}
