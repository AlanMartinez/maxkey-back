using Maxkeys.Application.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;
using Microsoft.Extensions.Options;

namespace Maxkeys.Application.Tests.Catalog;

[Collection(PostgresCollection.Name)]
public sealed class CreateProductTests
{
    private readonly PostgresFixture _fixture;
    private readonly ImageUrlBuilder _imageUrlBuilder;

    public CreateProductTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _imageUrlBuilder = new ImageUrlBuilder(
            Options.Create(new StorageOptions { R2PublicBaseUrl = "https://img.test" }));
    }

    /// <summary>Admin Product Creation — "Product created".</summary>
    [Fact]
    public async Task Creates_a_product_with_a_unique_slug()
    {
        var slug = $"product-{Guid.NewGuid():N}";

        await using var context = _fixture.CreateContext();
        var sut = new CreateProduct(context, _imageUrlBuilder);

        var created = await sut.ExecuteAsync(slug, "New Product", "PSN", "A description", null, isActive: true);

        Assert.NotNull(created);
        Assert.Equal(slug, created!.Slug);
        Assert.Equal("New Product", created.Name);
        Assert.Empty(created.Variants);

        await using var verify = _fixture.CreateContext();
        Assert.NotNull(await verify.Products.FindAsync(created.Id));
    }

    /// <summary>Admin Product Creation — "Duplicate slug rejected".</summary>
    [Fact]
    public async Task Returns_null_for_a_duplicate_slug()
    {
        var slug = $"product-{Guid.NewGuid():N}";
        await using (var seed = _fixture.CreateContext())
        {
            CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true, slug: slug);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new CreateProduct(context, _imageUrlBuilder);

        var created = await sut.ExecuteAsync(slug, "Another Product", "PSN", null, null, isActive: true);

        Assert.Null(created);

        await using var verify = _fixture.CreateContext();
        Assert.Single(verify.Products, p => p.Slug == slug);
    }

    /// <summary>Admin Product Creation — "Invalid fields rejected".</summary>
    [Fact]
    public async Task Invalid_fields_throw_domain_exception_and_create_nothing()
    {
        var slug = $"product-{Guid.NewGuid():N}";

        await using var context = _fixture.CreateContext();
        var sut = new CreateProduct(context, _imageUrlBuilder);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(slug, string.Empty, "PSN", null, null, isActive: true));

        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(verify.Products, p => p.Slug == slug);
    }
}
