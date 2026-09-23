using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design section 5: UNIQUE(slug), INDEX(platform).</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Platform).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ImageKey).HasMaxLength(500);
        builder.Property(p => p.DetailImageKey).HasMaxLength(500);
        builder.Property(p => p.Description).HasColumnType("text").IsRequired();
        builder.Property(p => p.ActivationGuideId);

        builder.HasIndex(p => p.ActivationGuideId);
        builder.Property(p => p.ActivationType).HasMaxLength(100);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.Platform);
    }
}
