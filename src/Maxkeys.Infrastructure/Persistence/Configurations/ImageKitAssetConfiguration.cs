using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Durable ImageKit delivery registry keyed by opaque storage path.</summary>
public sealed class ImageKitAssetConfiguration : IEntityTypeConfiguration<ImageKitAsset>
{
    public void Configure(EntityTypeBuilder<ImageKitAsset> builder)
    {
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.FilePath).HasMaxLength(500).IsRequired();
        builder.HasIndex(asset => asset.FilePath).IsUnique();
    }
}
