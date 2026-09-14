using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Carousel;

/// <summary>
/// A homepage carousel slide linking to a catalog <see cref="Domain.Catalog.Product"/>
/// (carousel spec "Admin Slide Creation"; design D2). Optional
/// <see cref="Title"/>/<see cref="Caption"/>/<see cref="ImageKey"/> overrides fall back
/// to the linked product's fields when unset (carousel spec "Public Carousel Listing").
/// <see cref="Update"/> mirrors the constructor's invariants.
/// </summary>
public sealed class CarouselSlide : Entity
{
    public Guid ProductId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public string? Title { get; private set; }
    public string? Caption { get; private set; }
    public string? ImageKey { get; private set; }

    public CarouselSlide(
        Guid productId,
        int sortOrder,
        bool isActive = true,
        string? title = null,
        string? caption = null,
        string? imageKey = null)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainException("Carousel slide must reference a product.");
        }

        if (sortOrder < 0)
        {
            throw new DomainException("Carousel slide sort order must not be negative.");
        }

        ProductId = productId;
        SortOrder = sortOrder;
        IsActive = isActive;
        Title = Normalize(title);
        Caption = Normalize(caption);
        ImageKey = Normalize(imageKey);
    }

    /// <summary>Updates all mutable fields in place, including the linked product. Shares the constructor's invariants.</summary>
    public void Update(
        Guid productId,
        int sortOrder,
        bool isActive,
        string? title,
        string? caption,
        string? imageKey)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainException("Carousel slide must reference a product.");
        }

        if (sortOrder < 0)
        {
            throw new DomainException("Carousel slide sort order must not be negative.");
        }

        ProductId = productId;
        SortOrder = sortOrder;
        IsActive = isActive;
        Title = Normalize(title);
        Caption = Normalize(caption);
        ImageKey = Normalize(imageKey);
    }

    /// <summary>Blank overrides mean "no override" — trims surrounding whitespace otherwise.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
