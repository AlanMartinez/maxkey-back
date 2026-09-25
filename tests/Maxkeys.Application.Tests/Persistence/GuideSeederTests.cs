using Maxkeys.Application.Tests.Fixtures;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class GuideSeederTests
{
    private readonly PostgresFixture _fixture;

    public GuideSeederTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Inserts_a_new_guide_from_the_seed_file()
    {
        var slug = $"guide-{Guid.NewGuid():N}";
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, $$"""
        { "guides": [ { "slug": "{{slug}}", "title": "Ejemplo de guía", "contentMarkdown": "## Paso 1" } ] }
        """);

        await using var context = _fixture.CreateContext();
        await GuideSeeder.SeedAsync(context, path);

        var guide = await context.ActivationGuides.SingleAsync(g => g.Slug == slug);
        Assert.Equal("Ejemplo de guía", guide.Title);
    }

    [Fact]
    public async Task Reseeding_the_same_slug_updates_in_place_without_duplicating()
    {
        var slug = $"guide-{Guid.NewGuid():N}";
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, $$"""{ "guides": [ { "slug": "{{slug}}", "title": "V1", "contentMarkdown": "v1" } ] }""");
        await using var context = _fixture.CreateContext();
        await GuideSeeder.SeedAsync(context, path);

        await File.WriteAllTextAsync(path, $$"""{ "guides": [ { "slug": "{{slug}}", "title": "V2", "contentMarkdown": "v2" } ] }""");
        await GuideSeeder.SeedAsync(context, path);

        var guides = context.ActivationGuides.Where(g => g.Slug == slug).ToList();
        Assert.Single(guides);
        Assert.Equal("V2", guides[0].Title);
    }
}
