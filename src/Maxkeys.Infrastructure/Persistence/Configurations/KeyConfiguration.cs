using Maxkeys.Domain.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design section 5: EncryptedCode bytea, enum as text, INDEX(order_item_id) via
/// the owning FK convention (see OrderItemConfiguration), INDEX(product_variant_id, status).
/// </summary>
public sealed class KeyConfiguration : IEntityTypeConfiguration<Key>
{
    public void Configure(EntityTypeBuilder<Key> builder)
    {
        builder.HasKey(k => k.Id);

        builder.Property(k => k.EncryptedCode).HasColumnType("bytea").IsRequired();
        builder.Property(k => k.LoadedBy).HasMaxLength(100).IsRequired();
        builder.Property(k => k.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(k => new { k.ProductVariantId, k.Status });

        builder.Property<uint>("xmin").IsRowVersion();
    }
}
