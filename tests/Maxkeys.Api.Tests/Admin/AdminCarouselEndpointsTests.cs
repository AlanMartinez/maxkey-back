using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maxkeys.Api.Tests.Auth;
using Maxkeys.Application.Carousel;
using Maxkeys.Domain.Carousel;
using Maxkeys.Domain.Catalog;
using Maxkeys.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Maxkeys.Api.Tests.Admin;

/// <summary>
/// Covers the carousel spec: Admin Slide Creation, Admin Slide Update and Removal,
/// Admin Carousel Authorization.
/// </summary>
[Collection(Hs256ApiCollection.Name)]
public sealed class AdminCarouselEndpointsTests
{
    private const string NonAdminSub = "44444444-4444-4444-4444-444444444444";

    private readonly Hs256ApiTestFixture _factory;

    public AdminCarouselEndpointsTests(Hs256ApiTestFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_list_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/admin/carousel");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_sub_is_forbidden_from_list()
    {
        var response = await AdminClient(NonAdminSub).GetAsync("/admin/carousel");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_listing_includes_inactive_slides()
    {
        Guid inactiveSlideId = default;
        await Seed(async db =>
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Product", UniquePlatform());
            db.Products.Add(product);
            var slide = new CarouselSlide(product.Id, sortOrder: 0, isActive: false);
            db.CarouselSlides.Add(slide);
            await db.SaveChangesAsync();
            inactiveSlideId = slide.Id;
        });

        var response = await AdminClient().GetAsync("/admin/carousel");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var slides = await response.Content.ReadFromJsonAsync<List<AdminCarouselSlide>>();
        Assert.Contains(slides!, s => s.Id == inactiveSlideId && !s.IsActive);
    }

    /// <summary>Admin Slide Creation — "Slide created for an existing product".</summary>
    [Fact]
    public async Task Admin_can_create_a_slide_for_an_existing_product()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().PostAsJsonAsync("/admin/carousel", new
        {
            productId,
            sortOrder = 1,
            isActive = true,
            title = (string?)null,
            caption = (string?)null,
            imageKey = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var slide = await response.Content.ReadFromJsonAsync<AdminCarouselSlide>();
        Assert.Equal(productId, slide!.ProductId);
    }

    [Fact]
    public async Task Create_canonicalizes_leading_slash_ImageKit_key_and_emits_ImageKit_delivery_url()
    {
        var productId = await SeedProductAsync();
        var imageKey = $"carousel/{Guid.NewGuid():N}.png";
        var registration = await AdminClient().PostAsJsonAsync("/admin/media/imagekit-assets", new { filePath = imageKey });
        Assert.Equal(HttpStatusCode.NoContent, registration.StatusCode);

        var response = await AdminClient().PostAsJsonAsync("/admin/carousel", new
        {
            productId,
            sortOrder = 0,
            isActive = true,
            imageKey = $"/{imageKey}",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var slide = await response.Content.ReadFromJsonAsync<AdminCarouselSlide>();
        Assert.Equal(imageKey, slide!.ImageKey);
        Assert.Equal($"https://ik.imagekit.io/test-account/{imageKey}", slide.ImageUrl);
    }

    /// <summary>Admin Slide Creation — "Slide creation rejects unknown product".</summary>
    [Fact]
    public async Task Create_with_unknown_product_returns_422()
    {
        var response = await AdminClient().PostAsJsonAsync("/admin/carousel", new
        {
            productId = Guid.NewGuid(),
            sortOrder = 0,
            isActive = true,
            title = (string?)null,
            caption = (string?)null,
            imageKey = (string?)null,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Admin Slide Update and Removal — "Slide reordered".</summary>
    [Fact]
    public async Task Admin_can_reorder_a_slide()
    {
        var productId = await SeedProductAsync();
        var slideId = await SeedSlideAsync(productId, sortOrder: 2);

        var response = await AdminClient().PutAsJsonAsync($"/admin/carousel/{slideId}", new
        {
            productId,
            sortOrder = 0,
            isActive = true,
            title = (string?)null,
            caption = (string?)null,
            imageKey = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var slide = await response.Content.ReadFromJsonAsync<AdminCarouselSlide>();
        Assert.Equal(0, slide!.SortOrder);
    }

    [Fact]
    public async Task Update_with_unknown_slide_id_returns_404()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().PutAsJsonAsync($"/admin/carousel/{Guid.NewGuid()}", new
        {
            productId,
            sortOrder = 0,
            isActive = true,
            title = (string?)null,
            caption = (string?)null,
            imageKey = (string?)null,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Admin Slide Update and Removal — "Slide deleted".</summary>
    [Fact]
    public async Task Admin_can_delete_a_slide()
    {
        var productId = await SeedProductAsync();
        var slideId = await SeedSlideAsync(productId, sortOrder: 0);

        var deleteResponse = await AdminClient().DeleteAsync($"/admin/carousel/{slideId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await AdminClient().GetAsync("/admin/carousel");
        var slides = await listResponse.Content.ReadFromJsonAsync<List<AdminCarouselSlide>>();
        Assert.DoesNotContain(slides!, s => s.Id == slideId);
    }

    [Fact]
    public async Task Delete_with_unknown_id_returns_404()
    {
        var response = await AdminClient().DeleteAsync($"/admin/carousel/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_unsafe_image_key_before_persistence()
    {
        var productId = await SeedProductAsync();

        var response = await AdminClient().PostAsJsonAsync("/admin/carousel", new
        {
            productId,
            sortOrder = 0,
            isActive = true,
            imageKey = "carousel/uploads/../private.png",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string UniquePlatform() => $"platform-{Guid.NewGuid():N}";

    private async Task<Guid> SeedProductAsync()
    {
        Guid id = default;
        await Seed(async db =>
        {
            var product = new Product($"p-{Guid.NewGuid():N}", "Product", UniquePlatform());
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        });
        return id;
    }

    private async Task<Guid> SeedSlideAsync(Guid productId, int sortOrder)
    {
        Guid id = default;
        await Seed(async db =>
        {
            var slide = new CarouselSlide(productId, sortOrder);
            db.CarouselSlides.Add(slide);
            await db.SaveChangesAsync();
            id = slide.Id;
        });
        return id;
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
