using System.Data.Common;

namespace LedgerCore.Application.Abstractions.Data;

/// <summary>
/// Read side of CQRS: queries open a plain connection and run hand-written SQL with Dapper,
/// bypassing EF Core change tracking. Writes never go through this.
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
