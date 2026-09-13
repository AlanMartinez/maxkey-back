using Maxkeys.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design section 5: INDEX(order_id) comes from the owning FK convention.</summary>
public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(i => i.VariantNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnType("numeric(12,2)");

        builder.Navigation(i => i.Keys)
            .HasField("_keys")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.Keys)
            .WithOne()
            .HasForeignKey(k => k.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
