using Maxkeys.Domain.Catalog;
using Maxkeys.Domain.Wishlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>
/// Wishlist spec: unique per (user, product) — enforced by a unique index, not
/// application logic (<c>AddToWishlist</c> is idempotent on top of it). FK to
/// <see cref="Product"/> with Restrict delete — matches
/// <c>CarouselSlideConfiguration</c>'s convention for a reference-only FK with
/// no navigation property.
/// </summary>
public sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.UserId, item.ProductId }).IsUnique();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
