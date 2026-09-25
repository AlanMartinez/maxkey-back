using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Guides;
using Maxkeys.Domain.Guides;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

[Collection(Hs256ApiCollection.Name)]
public sealed class AdminGuidesEndpointsTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AdminGuidesEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_list_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/guides");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_list()
    {
        var response = await AdminClient(NonAdminSub).GetAsync("/admin/guides");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_a_guide()
    {
        var slug = $"guide-{Guid.NewGuid():N}";

        var response = await AdminClient().PostAsJsonAsync("/admin/guides", new { slug, title = "Cómo activar", contentMarkdown = "## Paso 1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var guide = await response.Content.ReadFromJsonAsync<GuideDto>();
        Assert.Equal(slug, guide!.Slug);
    }

    [Fact]
    public async Task Create_with_duplicate_slug_returns_409()
    {
        var slug = await SeedGuideAsync();

        var response = await AdminClient().PostAsJsonAsync("/admin/guides", new { slug, title = "Other", contentMarkdown = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_a_guide()
    {
        var (id, _) = await SeedGuideWithIdAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/guides/{id}", new { slug = $"guide-{Guid.NewGuid():N}", title = "New title", contentMarkdown = "New content" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var guide = await response.Content.ReadFromJsonAsync<GuideDto>();
        Assert.Equal("New title", guide!.Title);
    }

    [Fact]
    public async Task Update_with_unknown_id_returns_404()
    {
        var response = await AdminClient().PutAsJsonAsync($"/admin/guides/{Guid.NewGuid()}", new { slug = $"guide-{Guid.NewGuid():N}", title = "Title", contentMarkdown = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_delete_an_unreferenced_guide()
    {
        var (id, _) = await SeedGuideWithIdAsync();

        var response = await AdminClient().DeleteAsync($"/admin/guides/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_with_unknown_id_returns_404()
    {
        var response = await AdminClient().DeleteAsync($"/admin/guides/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Review Focus: deleting a guide still linked to a product must fail, not orphan the FK.</summary>
    [Fact]
    public async Task Delete_of_a_referenced_guide_returns_409()
    {
        var (guideId, _) = await SeedGuideWithIdAsync();
        await Seed(async db =>
        {
            var product = new Maxkeys.Domain.Catalog.Product($"p-{Guid.NewGuid():N}", "Product", $"platform-{Guid.NewGuid():N}");
            db.Products.Add(product);
            await db.SaveChangesAsync();
            product.UpdateCatalogInfo(product.Name, product.Platform, product.Description, product.ImageKey, product.DetailImageKey, product.IsActive, guideId, product.ActivationType);
            await db.SaveChangesAsync();
        });

        var response = await AdminClient().DeleteAsync($"/admin/guides/{guideId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<string> SeedGuideAsync()
    {
        var (_, slug) = await SeedGuideWithIdAsync();
        return slug;
    }

    private async Task<(Guid Id, string Slug)> SeedGuideWithIdAsync()
    {
        Guid id = default;
        var slug = $"guide-{Guid.NewGuid():N}";
        await Seed(async db =>
        {
            var guide = new ActivationGuide(slug, "Guide", "content");
            db.ActivationGuides.Add(guide);
            await db.SaveChangesAsync();
            id = guide.Id;
        });
        return (id, slug);
    }

    private async Task Seed(Func<AppDbContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
    }

    private HttpClient AdminClient(string? sub = null)
    {
        var token = TestTokens.CreateHs256(
            sub ?? Hs256ApiTestFixture.AdminSub, Hs256ApiTestFixture.Issuer, Hs256ApiTestFixture.Audience, Hs256ApiTestFixture.Hs256Secret);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
