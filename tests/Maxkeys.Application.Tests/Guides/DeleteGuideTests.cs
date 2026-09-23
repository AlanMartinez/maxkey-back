using Maxkeys.Application.Guides;
using Maxkeys.Application.Tests.Catalog;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;

namespace Maxkeys.Application.Tests.Guides;

[Collection(PostgresCollection.Name)]
public sealed class DeleteGuideTests
{
    private readonly PostgresFixture _fixture;

    public DeleteGuideTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Deletes_an_unreferenced_guide()
    {
        Guid id;
        await using (var seed = _fixture.CreateContext())
        {
            var guide = GuideTestData.SeedGuide(seed);
            id = guide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new DeleteGuide(context);

        var deleted = await sut.ExecuteAsync(id);

        Assert.True(deleted);
        await using var verify = _fixture.CreateContext();
        Assert.Null(await verify.ActivationGuides.FindAsync(id));
    }

    [Fact]
    public async Task Returns_false_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new DeleteGuide(context);

        var deleted = await sut.ExecuteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }

    /// <summary>Review Focus: a guide still linked from a product must not be deletable out from under it.</summary>
    [Fact]
    public async Task Rejects_deleting_a_guide_still_referenced_by_a_product()
    {
        Guid guideId;
        await using (var seed = _fixture.CreateContext())
        {
            var guide = GuideTestData.SeedGuide(seed);
            guideId = guide.Id;
            var product = CatalogTestData.SeedProduct(seed, CatalogTestData.UniquePlatform(), isActive: true);
            await seed.SaveChangesAsync();
            product.UpdateCatalogInfo(product.Name, product.Platform, product.Description, product.ImageKey, product.DetailImageKey, product.IsActive, guideId, product.ActivationType);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new DeleteGuide(context);

        await Assert.ThrowsAsync<DomainConflictException>(() => sut.ExecuteAsync(guideId));

        await using var verify = _fixture.CreateContext();
        Assert.NotNull(await verify.ActivationGuides.FindAsync(guideId));
    }
}
