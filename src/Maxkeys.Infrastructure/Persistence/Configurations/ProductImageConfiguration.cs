using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design section 5 pattern (same as <see cref="ProductVariantConfiguration"/>): INDEX(product_id), no FK.</summary>
public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ImageKey).HasMaxLength(500).IsRequired();

        builder.HasIndex(i => i.ProductId);
    }
}
