using Maxkeys.Application.Guides;
using Maxkeys.Application.Tests.Fixtures;

namespace Maxkeys.Application.Tests.Guides;

[Collection(PostgresCollection.Name)]
public sealed class GetGuideBySlugTests
{
    private readonly PostgresFixture _fixture;

    public GetGuideBySlugTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Returns_the_guide_for_a_known_slug()
    {
        var slug = GuideTestData.UniqueSlug();
        await using (var seed = _fixture.CreateContext())
        {
            GuideTestData.SeedGuide(seed, slug, title: "Título");
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new GetGuideBySlug(context);

        var guide = await sut.ExecuteAsync(slug);

        Assert.Equal("Título", guide!.Title);
    }

    [Fact]
    public async Task Returns_null_for_unknown_slug()
    {
        await using var context = _fixture.CreateContext();
        var sut = new GetGuideBySlug(context);

        Assert.Null(await sut.ExecuteAsync("unknown-slug-that-does-not-exist"));
    }
}
