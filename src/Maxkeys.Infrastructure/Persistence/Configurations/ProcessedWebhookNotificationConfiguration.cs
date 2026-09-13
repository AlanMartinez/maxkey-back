using Maxkeys.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design section 5: PK(request_id) — dedupe by PK violation, not read-then-write.</summary>
public sealed class ProcessedWebhookNotificationConfiguration : IEntityTypeConfiguration<ProcessedWebhookNotification>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookNotification> builder)
    {
        builder.HasKey(n => n.RequestId);

        builder.Property(n => n.RequestId).HasMaxLength(128);
        builder.Property(n => n.PaymentId).HasMaxLength(100).IsRequired();
        builder.Property(n => n.OrderId);
        builder.Property(n => n.ReceivedAt);
    }
}
