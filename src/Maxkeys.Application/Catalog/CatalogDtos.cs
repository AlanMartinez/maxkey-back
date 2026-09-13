namespace Maxkeys.Application.Catalog;

/// <summary>
/// Catalog list item (design section 7, <c>GET /catalog/products</c> response
/// item). PascalCase property names serialize to the exact camelCase names in
/// the API contract (frontend mirror: <c>types/api.ts</c> <c>ProductSummary</c>).
/// <see cref="FromPrice"/>/<see cref="OldPrice"/> are taken from the product's
/// cheapest active variant.
/// </summary>
public sealed record ProductSummary(
    Guid Id,
    string Slug,
    string Name,
    string Platform,
    string ImageUrl,
    decimal FromPrice,
    decimal? OldPrice);

/// <summary>
/// A purchasable variant as exposed on the product detail page (design section 7
/// <c>GET /catalog/products/{slug}</c> response <c>variants[]</c>; frontend
/// mirror: <c>ProductVariantDto</c>). No stock counter is ever included (catalog
/// spec "Product Variant Exposure"). <see cref="ProductVariant"/> has no stored
/// display name, so <see cref="Name"/> is composed from <see cref="Region"/> and
/// <see cref="Edition"/> by <c>GetProductBySlug</c>.
/// </summary>
public sealed record ProductVariantDetail(
    Guid Id,
    string Name,
    string? Region,
    string? Edition,
    decimal Price,
    decimal? OldPrice,
    string Currency);

/// <summary>
/// Full product detail (design section 7 <c>GET /catalog/products/{slug}</c>
/// response; frontend mirror: <c>ProductDetail extends ProductSummary</c>).
/// <see cref="Description"/> is mapped from <c>Product.Description</c> (added
/// in PR12 task 12.0 — the proposal's ERD listed it but PR1 did not add it to
/// <c>Product</c>; it was empty for every product created before that column
/// existed).
/// </summary>
public sealed record ProductDetail(
    Guid Id,
    string Slug,
    string Name,
    string Platform,
    string ImageUrl,
    decimal FromPrice,
    decimal? OldPrice,
    string Description,
    IReadOnlyList<ProductVariantDetail> Variants);
