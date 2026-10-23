using System.Data.Common;
using LedgerCore.Application.Abstractions.Data;
using Microsoft.Data.SqlClient;

namespace LedgerCore.Infrastructure.Data;

internal sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
