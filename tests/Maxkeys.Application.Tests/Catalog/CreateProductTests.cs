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

    [Fact]
    public async Task Creates_a_product_with_an_existing_activation_guide()
    {
        Guid guideId;
        await using (var seed = _fixture.CreateContext())
        {
            var guide = new Maxkeys.Domain.Guides.ActivationGuide($"guide-{Guid.NewGuid():N}", "Guide");
            seed.ActivationGuides.Add(guide);
            guideId = guide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new CreateProduct(context, _imageUrlBuilder);

        var created = await sut.ExecuteAsync(
            $"product-{Guid.NewGuid():N}", "Name", CatalogTestData.UniquePlatform(), null, null, null, isActive: true,
            activationGuideId: guideId);

        Assert.Equal(guideId, created.ActivationGuideId);
    }

    /// <summary>Review Focus (final review, Important #2): the admin API must not create a dangling ActivationGuideId link.</summary>
    [Fact]
    public async Task Rejects_an_unknown_activation_guide_id_without_persisting_the_product()
    {
        var slug = $"product-{Guid.NewGuid():N}";
        await using var context = _fixture.CreateContext();
        var sut = new CreateProduct(context, _imageUrlBuilder);

        await Assert.ThrowsAsync<DomainException>(
            () => sut.ExecuteAsync(slug, "Name", CatalogTestData.UniquePlatform(), null, null, null, isActive: true, activationGuideId: Guid.NewGuid()));

        await using var verify = _fixture.CreateContext();
        Assert.DoesNotContain(verify.Products, p => p.Slug == slug);
    }
}
