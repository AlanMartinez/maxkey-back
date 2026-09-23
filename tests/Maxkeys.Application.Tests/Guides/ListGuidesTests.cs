using Maxkeys.Application.Guides;
using Maxkeys.Application.Tests.Fixtures;

namespace Maxkeys.Application.Tests.Guides;

[Collection(PostgresCollection.Name)]
public sealed class ListGuidesTests
{
    private readonly PostgresFixture _fixture;

    public ListGuidesTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lists_every_guide()
    {
        var titleA = $"A-{Guid.NewGuid():N}";
        var titleB = $"B-{Guid.NewGuid():N}";
        await using (var seed = _fixture.CreateContext())
        {
            GuideTestData.SeedGuide(seed, title: titleB);
            GuideTestData.SeedGuide(seed, title: titleA);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new ListGuides(context);

        var guides = await sut.ExecuteAsync();

        Assert.Contains(guides, g => g.Title == titleA);
        Assert.Contains(guides, g => g.Title == titleB);
    }
}
