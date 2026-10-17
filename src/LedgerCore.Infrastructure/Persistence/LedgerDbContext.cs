using LedgerCore.Application.Abstractions.Persistence;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Users;
using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.Infrastructure.Persistence;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }

    /// <summary>
    /// Model-wide type rules, so no single property can forget them (spec rules 4 and 7).
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>()
            .HavePrecision(Money.Precision, Money.Scale);

        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("datetime2(3)")
            .HaveConversion<UtcDateTimeConverter>();

        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("datetime2(3)")
            .HaveConversion<UtcDateTimeConverter>();
    }
}
