namespace Maxkeys.Application.Catalog;

/// <summary>
/// A variant as exposed to the admin catalog view (design D3 contract table).
/// Unlike <see cref="ProductVariantDetail"/>, this carries every admin-editable
/// field, including <see cref="IsActive"/>, and has no computed display name.
/// </summary>
public sealed record AdminVariant(
    Guid Id,
    string? Region,
    string? Edition,
    decimal Price,
    decimal? OldPrice,
    string Currency,
    int SortOrder,
    bool IsActive);

/// <summary>
/// A product as exposed to the admin catalog view (design D3 contract table,
/// <c>GET /admin/catalog/products</c> response item). Unlike <see cref="ProductSummary"/>,
/// this includes inactive products and every variant regardless of
/// <see cref="AdminVariant.IsActive"/> (admin-catalog spec "Admin Product Listing
/// Including Inactive").
/// </summary>
public sealed record AdminProduct(
    Guid Id,
    string Slug,
    string Name,
    string Platform,
    bool IsActive,
    string? ImageKey,
    string ImageUrl,
    string? DetailImageKey,
    string DetailImageUrl,
    string Description,
    IReadOnlyList<AdminVariant> Variants,
    IReadOnlyList<string> ImageKeys,
    IReadOnlyList<string> Images,
    string? ActivationGuideUrl,
    string? ActivationType);
