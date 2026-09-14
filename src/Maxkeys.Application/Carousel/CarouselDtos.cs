namespace Maxkeys.Application.Carousel;

/// <summary>
/// A public carousel slide (carousel spec "Public Carousel Listing"; design contract
/// table, <c>GET /catalog/carousel</c> response item). <see cref="Title"/>/<see cref="Caption"/>
/// already resolve the slide's override vs. its product's fields.
/// </summary>
public sealed record CarouselSlideSummary(
    Guid Id,
    string Title,
    string? Caption,
    string ImageUrl,
    string ProductSlug,
    int SortOrder);

/// <summary>
/// A carousel slide as exposed to the admin view (design contract table,
/// <c>GET /admin/carousel</c> response item). Carries the raw override fields plus
/// enough product context for the admin UI to render without a second call.
/// </summary>
public sealed record AdminCarouselSlide(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    bool ProductIsActive,
    int SortOrder,
    bool IsActive,
    string? Title,
    string? Caption,
    string? ImageKey,
    string ImageUrl);
