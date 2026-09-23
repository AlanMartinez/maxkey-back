using Maxkeys.Application.Guides;
using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Domain.Common;

namespace Maxkeys.Application.Tests.Guides;

[Collection(PostgresCollection.Name)]
public sealed class UpdateGuideTests
{
    private readonly PostgresFixture _fixture;

    public UpdateGuideTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Updates_title_and_content()
    {
        Guid id;
        await using (var seed = _fixture.CreateContext())
        {
            var guide = GuideTestData.SeedGuide(seed);
            id = guide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateGuide(context);

        var updated = await sut.ExecuteAsync(id, GuideTestData.UniqueSlug(), "New title", "New content");

        Assert.Equal("New title", updated!.Title);
    }

    [Fact]
    public async Task Returns_null_for_unknown_id()
    {
        await using var context = _fixture.CreateContext();
        var sut = new UpdateGuide(context);

        var updated = await sut.ExecuteAsync(Guid.NewGuid(), GuideTestData.UniqueSlug(), "Title", null);

        Assert.Null(updated);
    }

    [Fact]
    public async Task Rejects_a_slug_already_used_by_another_guide()
    {
        var takenSlug = GuideTestData.UniqueSlug();
        Guid id;
        await using (var seed = _fixture.CreateContext())
        {
            GuideTestData.SeedGuide(seed, takenSlug);
            var guide = GuideTestData.SeedGuide(seed);
            id = guide.Id;
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var sut = new UpdateGuide(context);

        await Assert.ThrowsAsync<DomainConflictException>(() => sut.ExecuteAsync(id, takenSlug, "Title", null));
    }
}
