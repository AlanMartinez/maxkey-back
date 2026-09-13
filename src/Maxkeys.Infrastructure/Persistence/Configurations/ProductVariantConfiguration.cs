using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design section 5: numeric(12,2) money, INDEX(product_id).</summary>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Price).HasColumnType("numeric(12,2)");
        builder.Property(v => v.OldPrice).HasColumnType("numeric(12,2)");
        builder.Property(v => v.Currency).HasMaxLength(3).IsRequired();
        builder.Property(v => v.Region).HasMaxLength(100);
        builder.Property(v => v.Edition).HasMaxLength(100);

        builder.HasIndex(v => v.ProductId);
    }
}
