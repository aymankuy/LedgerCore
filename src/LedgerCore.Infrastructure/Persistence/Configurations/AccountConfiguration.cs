using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Common;
using LedgerCore.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerCore.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", t =>
        {
            t.HasCheckConstraint("CK_Accounts_Status", "[Status] IN (1, 2, 3)");
            t.HasCheckConstraint("CK_Accounts_Kind", "[Kind] IN (1, 2)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Currency)
            .HasConversion(currency => currency.Code, code => Currency.From(code))
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(a => a.Status).IsRequired();
        builder.Property(a => a.Kind).HasDefaultValue(AccountKind.Customer);
        builder.Property(a => a.AllowsOverdraft).HasDefaultValue(false);
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        // Exactly one settlement account per currency, so lookups by currency are unambiguous.
        builder.HasIndex(a => a.Currency)
            .IsUnique()
            .HasFilter("[Kind] = 2")
            .HasDatabaseName("UX_Accounts_SettlementPerCurrency");

        // Shadow property: the database needs it for optimistic concurrency, the domain does not.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
