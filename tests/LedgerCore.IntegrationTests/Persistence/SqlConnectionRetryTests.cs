using FluentAssertions;
using LedgerCore.Application.Abstractions.Data;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Data;
using LedgerCore.Infrastructure.Persistence;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class SqlConnectionRetryTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Opening_is_retried_on_a_transient_error_before_it_fails()
    {
        // Error 4060, "cannot open database", is on the transient list: a database that does not
        // exist yet answers like one that is still resuming. This proves the retry logic is
        // active in this SqlClient version, not only configured.
        var provider = SqlConnectionRetry.Create(numberOfTries: 3, delta: TimeSpan.FromMilliseconds(100), max: TimeSpan.FromMilliseconds(200));
        var retries = 0;
        provider.Retrying += (_, _) => retries++;

        await using var connection = new SqlConnection(Database($"missing_{Guid.NewGuid():N}")) { RetryLogicProvider = provider };
        var open = () => connection.OpenAsync();

        await open.Should().ThrowAsync<AggregateException>();
        retries.Should().Be(2, "three tries means two retries");
    }

    [Fact]
    public async Task Both_the_read_side_and_ef_core_open_connections_with_the_retry()
    {
        await using var services = new ServiceCollection().AddInfrastructure(fixture.ConnectionString).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        await using var dapper = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().OpenConnectionAsync(CancellationToken.None);
        ((SqlConnection)dapper).RetryLogicProvider.Should().BeSameAs(SqlConnectionRetry.Provider);

        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await db.Database.OpenConnectionAsync();
        ((SqlConnection)db.Database.GetDbConnection()).RetryLogicProvider.Should().BeSameAs(SqlConnectionRetry.Provider);
    }

    private string Database(string name) =>
        new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name, ConnectTimeout = 5 }.ConnectionString;
}
