using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerCore.Infrastructure.Persistence.Configurations;

internal sealed class AccountBalanceSnapshotConfiguration : IEntityTypeConfiguration<AccountBalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<AccountBalanceSnapshot> builder)
    {
        builder.ToTable("AccountBalanceSnapshots");

        builder.HasKey(s => s.AccountId);
        builder.Property(s => s.AccountId).ValueGeneratedNever();
        builder.Property(s => s.Balance).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<AccountBalanceSnapshot>(s => s.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BalanceSnapshotRunConfiguration : IEntityTypeConfiguration<BalanceSnapshotRun>
{
    public void Configure(EntityTypeBuilder<BalanceSnapshotRun> builder)
    {
        builder.ToTable("BalanceSnapshotRuns");

        // Clustered on the identity, so "the latest run" is a one-row seek from the end.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityColumn();
        builder.Property(r => r.StartedAtUtc).IsRequired();
        builder.Property(r => r.FinishedAtUtc).IsRequired();
        builder.Property(r => r.FromEntryId).IsRequired();
        builder.Property(r => r.ToEntryId).IsRequired();
        builder.Property(r => r.AccountsUpdated).IsRequired();
    }
}
