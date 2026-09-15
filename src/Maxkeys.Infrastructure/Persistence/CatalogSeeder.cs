using System.Text.Json;
using System.Text.Json.Serialization;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Infrastructure.Persistence;

/// <summary>
/// Upserts the operator-maintained catalog seed file into <see cref="AppDbContext"/>
/// (ADR-12 — no catalog admin UI in the MVP; a JSON file reviewed in PRs is the
/// simplest auditable source). Products are matched by <see cref="Product.Slug"/>;
/// variants are matched within a product by the <c>(Region, Edition)</c> pair,
/// since that is the only stable business key a variant has (design section 4.1
/// — variants have no stored display name or seed-provided id). Re-running the
/// seeder against the same file is idempotent: existing rows are updated in
/// place via <see cref="Product.UpdateCatalogInfo"/>/<see cref="ProductVariant.UpdateDetails"/>,
/// never duplicated. Wired via <c>Maxkeys.Api --seed-catalog &lt;path&gt;</c>
/// (<c>Program.cs</c>).
/// </summary>
public static class CatalogSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task SeedAsync(AppDbContext db, string jsonFilePath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(jsonFilePath, cancellationToken);
        var document = JsonSerializer.Deserialize<CatalogSeedDocument>(json, JsonOptions)
            ?? new CatalogSeedDocument([]);

        foreach (var seedProduct in document.Products)
        {
            var product = await db.Products.SingleOrDefaultAsync(p => p.Slug == seedProduct.Slug, cancellationToken);

            if (product is null)
            {
                product = new Product(
                    seedProduct.Slug,
                    seedProduct.Name,
                    seedProduct.Platform,
                    seedProduct.IsActive,
                    seedProduct.ImageKey,
                    seedProduct.Description);
                db.Products.Add(product);
            }
            else
            {
                product.UpdateCatalogInfo(
                    seedProduct.Name,
                    seedProduct.Platform,
                    seedProduct.Description,
                    seedProduct.ImageKey,
                    seedProduct.IsActive);
            }

            var existingVariants = await db.ProductVariants
                .Where(v => v.ProductId == product.Id)
                .ToListAsync(cancellationToken);

            foreach (var seedVariant in seedProduct.Variants)
            {
                var existing = existingVariants.FirstOrDefault(
                    v => v.Region == seedVariant.Region && v.Edition == seedVariant.Edition);

                if (existing is null)
                {
                    db.ProductVariants.Add(new ProductVariant(
                        product.Id,
                        seedVariant.Price,
                        seedVariant.Currency,
                        seedVariant.DiscountPercentage,
                        seedVariant.Region,
                        seedVariant.Edition,
                        seedVariant.SortOrder,
                        seedVariant.IsActive));
                }
                else
                {
                    existing.UpdateDetails(
                        seedVariant.Price,
                        seedVariant.DiscountPercentage,
                        seedVariant.Currency,
                        seedVariant.Region,
                        seedVariant.Edition,
                        seedVariant.SortOrder,
                        seedVariant.IsActive);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed record CatalogSeedDocument(List<CatalogSeedProduct> Products);

internal sealed record CatalogSeedProduct(
    string Slug,
    string Name,
    string Platform,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("imageKey")] string? ImageKey,
    bool IsActive,
    List<CatalogSeedVariant> Variants);

internal sealed record CatalogSeedVariant(
    string? Region,
    string? Edition,
    decimal Price,
    decimal? DiscountPercentage,
    string Currency,
    int SortOrder,
    bool IsActive);
