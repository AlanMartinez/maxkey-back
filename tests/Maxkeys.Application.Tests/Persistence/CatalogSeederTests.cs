using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Guides;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class CatalogSeederTests
{
    private readonly PostgresFixture _fixture;

    public CatalogSeederTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Review Focus: the seed file no longer carries a guide field — reseeding an existing product must not null out an admin-assigned guide.</summary>
    [Fact]
    public async Task Reseeding_an_existing_product_preserves_its_admin_assigned_guide()
    {
        var slug = $"product-{Guid.NewGuid():N}";
        await using var context = _fixture.CreateContext();

        var guide = new ActivationGuide($"guide-{Guid.NewGuid():N}", "Guide", "content");
        context.ActivationGuides.Add(guide);
        await context.SaveChangesAsync();

        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, $$"""
        { "products": [ { "slug": "{{slug}}", "name": "Seed Product", "platform": "Steam", "isActive": true, "variants": [] } ] }
        """);
        await Maxkeys.Infrastructure.Persistence.CatalogSeeder.SeedAsync(context, path);

        var product = await context.Products.SingleAsync(p => p.Slug == slug);
        product.UpdateCatalogInfo(product.Name, product.Platform, product.Description, product.ImageKey, product.DetailImageKey, product.IsActive, guide.Id, product.ActivationType);
        await context.SaveChangesAsync();

        await Maxkeys.Infrastructure.Persistence.CatalogSeeder.SeedAsync(context, path);

        var reseeded = await context.Products.SingleAsync(p => p.Slug == slug);
        Assert.Equal(guide.Id, reseeded.ActivationGuideId);
    }
}
