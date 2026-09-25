using System.Net;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Guides;
using Maxkeys.Domain.Guides;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests;

[Collection(Hs256ApiCollection.Name)]
public sealed class GuideEndpointsTests
{
    private readonly Hs256ApiTestFixture _factory;

    public GuideEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Public_can_fetch_a_guide_by_slug_without_auth()
    {
        var slug = $"guide-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ActivationGuides.Add(new ActivationGuide(slug, "Título", "Contenido"));
            await db.SaveChangesAsync();
        }

        var response = await _factory.CreateClient().GetAsync($"/guides/{slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var guide = await response.Content.ReadFromJsonAsync<GuideDto>();
        Assert.Equal("Título", guide!.Title);
    }

    [Fact]
    public async Task Unknown_slug_returns_404()
    {
        var response = await _factory.CreateClient().GetAsync("/guides/unknown-slug-that-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
