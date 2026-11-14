using FluentAssertions;
using LedgerCore.Application;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence.Seeding;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class DatabaseInitializerTests(SqlServerFixture fixture)
{
    private const string AppLogin = "ledgercore_app_test";
    private const string AppLoginPassword = "Test_App_Login_9f3c!";

    [Fact]
    public async Task Initializer_is_idempotent_and_seeds_reference_data_once()
    {
        await InitializeAsync();
        await InitializeAsync();

        await using var db = fixture.CreateDbContext();
        var eur = Currency.From("EUR");
        (await db.Accounts.CountAsync(a => a.Kind == AccountKind.Settlement && a.Currency == eur)).Should().Be(1);
        (await db.Accounts.CountAsync(a => a.Kind == AccountKind.FeeRevenue && a.Currency == eur)).Should().Be(1);
        (await db.Users.CountAsync(u => u.Email == SeedUsers.SystemEmail)).Should().Be(1);
        (await db.Users.CountAsync(u => SeedUsers.DemoEmails.Contains(u.Email))).Should().Be(2);
    }

    [Theory]
    [InlineData("UPDATE dbo.LedgerEntries SET Amount = Amount + 1")]
    [InlineData("DELETE FROM dbo.LedgerEntries")]
    [InlineData("TRUNCATE TABLE dbo.LedgerEntries")]
    public async Task Application_login_cannot_change_or_remove_ledger_entries(string sql)
    {
        await InitializeAsync();

        await using var connection = new SqlConnection(AppLoginConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);

        var act = () => command.ExecuteNonQueryAsync();

        // 229 = permission denied on the object, 1088 = TRUNCATE needs ALTER on the table.
        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().BeOneOf(229, 1088);
    }

    [Fact]
    public async Task Application_login_can_read_and_post_through_the_procedure()
    {
        await InitializeAsync();

        await using var connection = new SqlConnection(AppLoginConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT HAS_PERMS_BY_NAME('dbo.usp_PostLedgerTransaction', 'OBJECT', 'EXECUTE') & HAS_PERMS_BY_NAME('dbo.LedgerEntries', 'OBJECT', 'SELECT')",
            connection);

        (await command.ExecuteScalarAsync()).Should().Be(1);
    }

    private async Task InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(fixture.ConnectionString);
        services.Configure<DatabaseOptions>(o =>
        {
            o.MigrateOnStartup = true;
            o.AppLogin = AppLogin;
            o.AppLoginPassword = AppLoginPassword;
            o.SeedDemoData = true;
            o.DemoPassword = "demo-password";
        });

        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();
    }

    private string AppLoginConnectionString() =>
        new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            UserID = AppLogin,
            Password = AppLoginPassword,
        }.ConnectionString;
}
