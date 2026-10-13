using LedgerCore.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerCore.Infrastructure.Persistence.Configurations;

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("LedgerTransactions", t => t.HasCheckConstraint("CK_LedgerTransactions_Type", "[Type] BETWEEN 1 AND 5"));

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Type).IsRequired();
        builder.Property(t => t.ReferenceId).HasMaxLength(LedgerTransaction.ReferenceIdMaxLength);
        builder.Property(t => t.Description).HasMaxLength(LedgerTransaction.DescriptionMaxLength);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        builder.Ignore(t => t.Net);

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Entries is a read-only view over the private _entries list; EF writes through the field.
        builder.Navigation(t => t.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
