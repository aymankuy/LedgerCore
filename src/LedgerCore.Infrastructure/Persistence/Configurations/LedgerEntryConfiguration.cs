using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerCore.Infrastructure.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries", t =>
        {
            t.HasCheckConstraint("CK_LedgerEntries_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_LedgerEntries_Direction", "[Direction] IN (1, 2)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityColumn();

        builder.Property(e => e.Direction).IsRequired();
        builder.Property(e => e.Amount).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();

        builder.Ignore(e => e.SignedAmount);

        // TR_LedgerEntries_NetZero sums the entries of each inserted transaction. Covering the
        // index lets it seek only its own rows. Without it, parallel posts deadlocked by reading
        // each other's uncommitted rows (see ADR 0001).
        builder.HasIndex(e => e.TransactionId)
            .IncludeProperties(e => new { e.Direction, e.Amount });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
