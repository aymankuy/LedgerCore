using LedgerCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace LedgerCore.IntegrationTests.Infrastructure;

/// <summary>
/// Starts one real SQL Server 2022 container for the whole test collection and applies all
/// migrations to it. Every run gets a fresh database, so tests never delete ledger rows,
/// which the append-only trigger would reject anyway.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public LedgerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
