using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>
/// Design section 5: numeric(12,2) money, INDEX(product_id). The partial unique
/// index on <c>product_id WHERE is_recommended</c> is the DB-level guarantee that
/// at most one variant per product is recommended (column names are snake_case
/// via <c>UseSnakeCaseNamingConvention</c>).
/// </summary>
public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Price).HasColumnType("numeric(12,2)");
        builder.Property(v => v.DiscountPercentage).HasColumnType("numeric(5,2)");
        builder.Property(v => v.Currency).HasMaxLength(3).IsRequired();
        builder.Property(v => v.Region).HasMaxLength(100);
        builder.Property(v => v.Edition).HasMaxLength(100);
        builder.Property(v => v.IsRecommended).HasDefaultValue(false);

        builder.HasIndex(v => v.ProductId);
        builder.HasIndex(v => v.ProductId, "ix_product_variants_product_id_is_recommended")
            .IsUnique()
            .HasDatabaseName("ix_product_variants_product_id_is_recommended")
            .HasFilter("is_recommended = TRUE");
    }
}
