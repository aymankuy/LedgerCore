using LedgerCore.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerCore.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyKeys");

        // The primary key is backed by a unique clustered index, which satisfies the spec's
        // "unique index on Key" without a second, redundant index.
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength);

        builder.Property(r => r.RequestHash)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(r => r.ResponseBody);
        builder.Property(r => r.StatusCode);
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // The Worker deletes keys older than 24 hours every hour; without this index each run
        // would scan the whole table.
        builder.HasIndex(r => r.CreatedAtUtc);
    }
}
