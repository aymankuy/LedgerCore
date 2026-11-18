using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerCore.Infrastructure.Persistence.Configurations;

internal sealed class AccountStatusChangeConfiguration : IEntityTypeConfiguration<AccountStatusChange>
{
    public void Configure(EntityTypeBuilder<AccountStatusChange> builder)
    {
        builder.ToTable("AccountStatusHistory", t => t.HasCheckConstraint("CK_AccountStatusHistory_Status", "[Status] IN (1, 2, 3)"));

        // Clustered on the identity: rows are only ever appended, and the warehouse load reads
        // everything after the last Id it has seen.
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityColumn();
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.ChangedAtUtc).IsRequired();

        builder.HasIndex(c => new { c.AccountId, c.Id });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(c => c.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
