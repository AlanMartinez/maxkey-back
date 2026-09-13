using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;

namespace Maxkeys.Application.Tests.Catalog;

/// <summary>Shared seed helpers for <c>GetCatalogTests</c> and <c>GetProductBySlugTests</c>.</summary>
internal static class CatalogTestData
{
    public static string UniquePlatform() => $"platform-{Guid.NewGuid():N}";

    public static Product SeedProduct(
        AppDbContext context,
        string platform,
        bool isActive,
        string? slug = null,
        string? name = null,
        string? imageKey = null,
        string? description = null)
    {
        slug ??= $"product-{Guid.NewGuid():N}";
        var product = new Product(slug, name ?? $"Product {slug}", platform, isActive, imageKey, description);
        context.Products.Add(product);
        return product;
    }

    public static void SeedVariant(
        AppDbContext context,
        Guid productId,
        decimal price,
        decimal? oldPrice = null,
        int sortOrder = 0,
        string? region = null,
        string? edition = null,
        bool isActive = true)
    {
        context.ProductVariants.Add(
            new ProductVariant(productId, price, "ARS", oldPrice, region, edition, sortOrder, isActive));
    }
}
