using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>Design D2: FK to <see cref="Product"/> with Restrict delete, index (is_active, sort_order).</summary>
public sealed class CarouselSlideConfiguration : IEntityTypeConfiguration<CarouselSlide>
{
    public void Configure(EntityTypeBuilder<CarouselSlide> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.Caption).HasMaxLength(500);
        builder.Property(s => s.ImageKey).HasMaxLength(500);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.IsActive, s.SortOrder });
    }
}
