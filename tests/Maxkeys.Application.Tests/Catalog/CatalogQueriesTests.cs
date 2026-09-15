using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

/// <summary>
/// PR12 task 12.5: image URL resolution stays absolute (catalog spec "Image
/// URL Resolution") and <see cref="CatalogSeeder"/> upserts are idempotent on
/// re-run (ADR-12).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogQueriesTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public CatalogQueriesTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    [Fact]
    public async Task GetCatalog_resolves_image_url_to_an_absolute_url_not_the_raw_key()
    {
        var platform = CatalogTestData.UniquePlatform();

        await using (var seed = _fixture.CreateContext())
        {
            var product = CatalogTestData.SeedProduct(seed, platform, isActive: true, imageKey: "products/raw-key.png");
            CatalogTestData.SeedVariant(seed, product.Id, price: 100m);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetCatalog(context, _imageUrlBuilder);

        var result = await sut.ExecuteAsync(platform: platform, q: null);

        var summary = Assert.Single(result);
        Assert.NotEqual("products/raw-key.png", summary.ImageUrl);
        Assert.Equal("https://img.test/products/raw-key.png", summary.ImageUrl);
    }

    [Fact]
    public async Task CatalogSeeder_upsert_is_idempotent_on_rerun()
    {
        var slug = $"seed-idempotent-{Guid.NewGuid():N}";
        var seedPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(seedPath, BuildSeedJson(slug, price: 1000m));
            await using (var context = _fixture.CreateContext())
            {
                await CatalogSeeder.SeedAsync(context, seedPath);
            }

            // Re-run with a changed price: the existing row must be updated in place, not duplicated.
            await File.WriteAllTextAsync(seedPath, BuildSeedJson(slug, price: 1500m));
            await using (var context = _fixture.CreateContext())
            {
                await CatalogSeeder.SeedAsync(context, seedPath);
            }

            await using var verifyContext = _fixture.CreateContext();
            var products = await verifyContext.Products.Where(p => p.Slug == slug).ToListAsync();
            var product = Assert.Single(products);

            var variants = await verifyContext.ProductVariants.Where(v => v.ProductId == product.Id).ToListAsync();
            var variant = Assert.Single(variants);
            Assert.Equal(1500m, variant.Price);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    private static string BuildSeedJson(string slug, decimal price) => $$"""
        {
          "products": [
            {
              "slug": "{{slug}}",
              "name": "Seed Idempotency Test Product",
              "platform": "steam",
              "description": "test description",
              "imageKey": "products/idempotent.png",
              "isActive": true,
              "variants": [
                { "region": "AR", "edition": "Standard", "price": {{price}}, "discountPercentage": null, "currency": "ARS", "sortOrder": 0, "isActive": true }
              ]
            }
          ]
        }
        """;
}
