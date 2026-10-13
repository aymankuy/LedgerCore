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

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
