using Maxkeys.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design section 5: Payload as jsonb, enum as text, INDEX(status, next_attempt_at) for the claim query.</summary>
public sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.LastError).HasMaxLength(2000);

        builder.HasIndex(e => new { e.Status, e.NextAttemptAt });
    }
}
