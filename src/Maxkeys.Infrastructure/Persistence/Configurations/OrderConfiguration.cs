using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design section 5: xmin concurrency token (ADR-05 UpdatedAt bump complements it),
/// enum as text, INDEX(user_id)/(status), UNIQUE(mp_payment_id) WHERE NOT NULL.
/// </summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.BuyerEmail).HasMaxLength(320).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
        builder.Property(o => o.TotalAmount).HasColumnType("numeric(12,2)");
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.MpPreferenceId).HasMaxLength(100);
        builder.Property(o => o.MpPaymentId).HasMaxLength(100);
        builder.Property(o => o.LastPaymentAttemptId).HasMaxLength(100);
        builder.Property(o => o.LastPaymentAttemptStatus).HasMaxLength(32);

        builder.Navigation(o => o.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.MpPaymentId).IsUnique().HasFilter("mp_payment_id IS NOT NULL");
    }
}
