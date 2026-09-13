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
/// <see cref="Description"/> is empty until the <c>Product</c> domain entity
/// gains a description column — the proposal's ERD listed it but PR1 did not
/// add it to <c>Product</c> (see this PR's apply-progress deviation note).
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
