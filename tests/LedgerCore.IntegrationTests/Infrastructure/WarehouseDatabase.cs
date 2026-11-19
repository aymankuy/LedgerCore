using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace LedgerCore.IntegrationTests.Infrastructure;

/// <summary>
/// A throwaway warehouse database on the test SQL Server, built from the scripts in
/// data/warehouse, so the tests run the same DDL and procedures that get deployed.
/// </summary>
public sealed class WarehouseDatabase : IAsyncDisposable
{
    private readonly string _masterConnectionString;
    private readonly string _name;

    private WarehouseDatabase(string masterConnectionString, string name, string connectionString)
    {
        _masterConnectionString = masterConnectionString;
        _name = name;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<WarehouseDatabase> CreateAsync(SqlServerFixture fixture)
    {
        var name = $"LedgerCoreDW_{Guid.NewGuid():N}";
        var master = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var own = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name }.ConnectionString;

        var database = new WarehouseDatabase(master, name, own);
        await database.RunAsync(master, $"CREATE DATABASE [{name}]");

        var scripts = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Warehouse"), "*.sql")
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var script in scripts)
        {
            // SQL Server accepts GO only from a client tool, so split the script into batches here.
            foreach (var batch in Regex.Split(await File.ReadAllTextAsync(script), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await database.RunAsync(own, batch);
                }
            }
        }

        return database;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    public async Task<List<object?[]>> RowsAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            rows.Add(values.Select(v => v is DBNull ? null : v).ToArray());
        }

        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await RunAsync(_masterConnectionString, $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]");
    }

    private async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlCommand Command(SqlConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
