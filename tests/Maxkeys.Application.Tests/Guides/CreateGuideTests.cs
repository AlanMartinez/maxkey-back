using Maxkeys.Application.Guides;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;

namespace Maxkeys.Application.Tests.Guides;

[Collection(PostgresCollection.Name)]
public sealed class CreateGuideTests
{
    private readonly PostgresFixture _fixture;

    public CreateGuideTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Creates_a_guide_with_a_unique_slug()
    {
        await using var context = _fixture.CreateContext();
        var sut = new CreateGuide(context);

        var guide = await sut.ExecuteAsync(GuideTestData.UniqueSlug(), "Cómo activar", "## Paso 1");

        Assert.Equal("Cómo activar", guide.Title);
        await using var verify = _fixture.CreateContext();
        Assert.NotNull(await verify.ActivationGuides.FindAsync(guide.Id));
    }

    [Fact]
    public async Task Rejects_a_duplicate_slug_with_domain_conflict()
    {
        var slug = GuideTestData.UniqueSlug();
        await using (var seed = _fixture.CreateContext())
        {
            GuideTestData.SeedGuide(seed, slug);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new CreateGuide(context);

        await Assert.ThrowsAsync<DomainConflictException>(() => sut.ExecuteAsync(slug, "Other title", null));
    }
}
