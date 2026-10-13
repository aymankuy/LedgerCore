using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LedgerCore.Infrastructure.Persistence;

/// <summary>
/// datetime2 carries no time zone, so SQL Server hands values back as DateTimeKind.Unspecified.
/// Every timestamp we store is UTC, so we stamp that back on when reading.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    toDatabase => toDatabase,
    fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc));
